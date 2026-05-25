using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Discovery;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;

namespace Rfx2Mqtt.Devices.Handlers;

/// <summary>
/// <b>EN:</b> Handler for Lighting2 devices (packet type 0x11). Covers Chacon/DIO, HomeEasy EU,
/// ANSLUT and Kambrook. Bidirectional:
/// <list type="bullet">
///   <item><b>RX</b>: publishes state on <c>{prefix}/sensor/chacon/{device_name}</c>.</item>
///   <item><b>TX</b>: subscribes <c>{prefix}/command/chacon/{device_name}</c> with payload
///         <c>{ "command": "on|off|set_level", "level": 0..15 }</c>.</item>
/// </list>
/// <br/>
/// <b>FR:</b> Handler pour les équipements Lighting2 (paquet type 0x11). Gère Chacon/DIO,
/// HomeEasy EU, ANSLUT et Kambrook. Bidirectionnel :
/// <list type="bullet">
///   <item><b>RX</b> : publie l'état sur <c>{prefix}/sensor/chacon/{device_name}</c>.</item>
///   <item><b>TX</b> : souscrit à <c>{prefix}/command/chacon/{device_name}</c> avec payload
///         <c>{ "command": "on|off|set_level", "level": 0..15 }</c>.</item>
/// </list>
///
/// <para><b>EN/FR:</b> Frame layout 0x11 (after header) / Format trame 0x11 (après header) :</para>
/// <list type="bullet">
///   <item>Data[0..3] = device ID (4 bytes / 4 octets)</item>
///   <item>Data[4] = unitcode (1-16, 0 = group)</item>
///   <item>Data[5] = cmnd (0=Off, 1=On, 2=SetLevel, 3=GroupOff, 4=GroupOn)</item>
///   <item>Data[6] = level 0-15 (dimmer)</item>
///   <item>Data[7] = filler (high nibble) + signal (low nibble)</item>
/// </list>
/// </summary>
public class Lighting2Handler : IPacketHandler
{
    private readonly ILogger<Lighting2Handler> _logger;
    private readonly IDeviceRepository _devices;
    private readonly PermitJoinState _permitJoin;
    private readonly MqttTopics _topics;
    private readonly RfxComProtocol _protocol;
    private readonly RfxComSerialService _serialService;
    private readonly HomeAssistantDiscoveryService _discovery;
    private readonly AvailabilityService _availability;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions _jsonDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Lighting2Handler(
        ILogger<Lighting2Handler> logger,
        IDeviceRepository devices,
        PermitJoinState permitJoin,
        MqttTopics topics,
        RfxComProtocol protocol,
        RfxComSerialService serialService,
        HomeAssistantDiscoveryService discovery,
        AvailabilityService availability)
    {
        _logger = logger;
        _devices = devices;
        _permitJoin = permitJoin;
        _topics = topics;
        _protocol = protocol;
        _serialService = serialService;
        _discovery = discovery;
        _availability = availability;
    }

    #region IPacketHandler - RX (réception état)

    public IReadOnlyList<byte> SupportedPacketTypes { get; } = [PacketTypes.Lighting2];

    public bool CanHandle(RfxComPacket packet) => packet.PacketType == PacketTypes.Lighting2;

    public async Task<IEnumerable<MqttMessage>> HandleAsync(RfxComPacket packet)
    {
        var messages = new List<MqttMessage>();

        try
        {
            if (packet.Data.Length < 8)
            {
                _logger.LogWarning("Paquet Lighting2 trop court ({Len} octets data, 8 attendus)", packet.Data.Length);
                return messages;
            }

            var d = packet.Data;
            var deviceId = $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}{d[3]:X2}";
            var unitCode = d[4];
            var commandRaw = d[5];
            var level = d[6];
            var signal = d[7] & 0x0F;

            var isOn = commandRaw == Lighting2Commands.On
                    || commandRaw == Lighting2Commands.GroupOn
                    || commandRaw == Lighting2Commands.SetLevel
                    || commandRaw == Lighting2Commands.SetGroupLevel;

            var data = new Lighting2Data
            {
                SensorType = $"0x11/{packet.SubType:X2}",
                Model = Lighting2SubTypes.GetModelName(packet.SubType),
                DeviceId = deviceId,
                UnitCode = unitCode,
                CommandRaw = commandRaw,
                Command = Lighting2Commands.Decode(commandRaw),
                State = isOn ? "ON" : "OFF",
                Level = level,
                SignalLevel = signal,
                ReceivedAt = packet.ReceivedAt
            };

            // Chercher le nom dans la config
            var configuredName = FindDeviceName(d[0], d[1], d[2], d[3], unitCode);

            // PermitJoin : si false et device non configuré → ignorer
            if (!_permitJoin.IsEnabled && configuredName is null)
            {
                _logger.LogDebug("Chacon [{Id}] unit={Unit} ignoré (PermitJoin=false, non configuré)", deviceId, unitCode);
                return messages;
            }

            var deviceName = configuredName ?? $"{deviceId}_{unitCode}";
            const string kind = MqttTopics.KindChacon;
            var topicBase = _topics.SensorBase(kind, deviceName);

            // Auto-discovery Home Assistant (une seule fois par device)
            await _discovery.PublishLighting2DiscoveryAsync(data, deviceName);

            // Signaler le device comme actif (availability)
            await _availability.ReportSeenAsync(
                $"{kind}/{deviceName}",
                _topics.SensorAvailability(kind, deviceName));

            // JSON complet
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            messages.Add(new MqttMessage(topicBase, json, Retain: true));

            // Valeurs individuelles
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, deviceName, MqttTopics.AttrState),
                data.State, Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, deviceName, MqttTopics.AttrCommand),
                data.Command, Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, deviceName, MqttTopics.AttrLevel),
                data.LevelPercent.ToString(CultureInfo.InvariantCulture), Retain: true));

            _logger.LogInformation(
                "Chacon {Model} [{Id}] unit={Unit} : {Command} state={State} level={Level}% (signal:{Sig})",
                data.Model, deviceId, unitCode, data.Command, data.State, data.LevelPercent, signal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur parsing paquet Lighting2 : {Hex}",
                RfxComProtocol.FormatHex(packet.RawBytes));
        }

        return messages;
    }

    #endregion

    #region TX - Envoi de commandes (MQTT → série)

    /// <summary>
    /// Traite une commande MQTT pour un switch Chacon/DIO.
    /// </summary>
    public async Task HandleCommandAsync(string deviceName, string payload, CancellationToken ct)
    {
        var device = _devices.Snapshot.Chacon
            .FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            _logger.LogWarning("Équipement Chacon '{Name}' non trouvé dans la configuration", deviceName);
            return;
        }

        ChaconCommandPayload? cmd;
        try
        {
            cmd = JsonSerializer.Deserialize<ChaconCommandPayload>(payload, _jsonDeserializeOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Payload Chacon invalide : {Payload}", payload);
            return;
        }

        if (cmd is null)
        {
            _logger.LogWarning("Payload Chacon vide");
            return;
        }

        var lighting2Command = cmd.ToLighting2Command();
        if (lighting2Command is null)
        {
            _logger.LogWarning("Commande Chacon inconnue : '{Command}'", cmd.Command);
            return;
        }

        try
        {
            var idBytes = device.GetIdBytes();
            var frame = _protocol.BuildLighting2Command(
                device.SubType, idBytes, device.UnitCode,
                lighting2Command.Value, cmd.Level ?? 0);

            _logger.LogInformation("Chacon TX → {Name} [{Id}] unit={Unit} : {Command}",
                device.Name, device.Id, device.UnitCode, cmd.Command.ToUpperInvariant());

            await _serialService.SendRawAsync(frame, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur envoi commande Chacon vers {Name}", device.Name);
        }
    }

    #endregion

    #region Helpers

    private string? FindDeviceName(byte id1, byte id2, byte id3, byte id4, byte unitCode)
    {
        foreach (var device in _devices.Snapshot.Chacon)
        {
            try
            {
                var idBytes = device.GetIdBytes();   // mis en cache sur ChaconDevice
                if (idBytes[0] == id1 && idBytes[1] == id2
                    && idBytes[2] == id3 && idBytes[3] == id4
                    && device.UnitCode == unitCode)
                {
                    return device.Name;
                }
            }
            catch { /* Config invalide */ }
        }
        return null;
    }

    #endregion
}

/// <summary>Payload de commande Chacon reçu via MQTT</summary>
public class ChaconCommandPayload
{
    /// <summary>Commande : on, off, set_level, group_on, group_off</summary>
    public string Command { get; set; } = "";

    /// <summary>Niveau dimmer 0-15 (optionnel, pour set_level)</summary>
    public byte? Level { get; set; }

    public byte? ToLighting2Command() => Command.ToLowerInvariant() switch
    {
        "on" => Lighting2Commands.On,
        "off" => Lighting2Commands.Off,
        "set_level" or "dim" => Lighting2Commands.SetLevel,
        "group_on" => Lighting2Commands.GroupOn,
        "group_off" => Lighting2Commands.GroupOff,
        _ => null
    };
}
