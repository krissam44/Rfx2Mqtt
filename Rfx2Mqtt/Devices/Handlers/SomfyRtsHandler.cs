using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;

namespace Rfx2Mqtt.Devices.Handlers;

/// <summary>
/// <b>EN:</b> Handler for Somfy RFY packets (type 0x1A). Bidirectional:
/// <list type="bullet">
///   <item><b>RX</b> (serial → MQTT): detects physical Somfy remote button presses and publishes
///         them as events on <c>{prefix}/event/somfy/{device_name}</c>.</item>
///   <item><b>TX</b> (MQTT → serial): subscribes <c>{prefix}/command/somfy/{device_name}</c> with
///         payload <c>{ "command": "up|down|stop|program" }</c> and emits RFY frames.</item>
/// </list>
/// <br/>
/// <b>FR:</b> Handler pour les paquets Somfy RFY (type 0x1A). Bidirectionnel :
/// <list type="bullet">
///   <item><b>RX</b> (série → MQTT) : détecte les appuis sur télécommandes physiques Somfy et les
///         publie en événements sur <c>{prefix}/event/somfy/{device_name}</c>.</item>
///   <item><b>TX</b> (MQTT → série) : souscrit à <c>{prefix}/command/somfy/{device_name}</c>,
///         payload <c>{ "command": "up|down|stop|program" }</c>, émet des trames RFY.</item>
/// </list>
/// </summary>
public class SomfyRtsHandler : IPacketHandler
{
    private readonly ILogger<SomfyRtsHandler> _logger;
    private readonly IDeviceRepository _devices;
    private readonly MqttTopics _topics;
    private readonly RfxComProtocol _protocol;
    private readonly RfxComSerialService _serialService;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SomfyRtsHandler(
        ILogger<SomfyRtsHandler> logger,
        IDeviceRepository devices,
        MqttTopics topics,
        RfxComProtocol protocol,
        RfxComSerialService serialService)
    {
        _logger = logger;
        _devices = devices;
        _topics = topics;
        _protocol = protocol;
        _serialService = serialService;
    }

    #region IPacketHandler - RX (télécommandes physiques → MQTT)

    public IReadOnlyList<byte> SupportedPacketTypes { get; } = [PacketTypes.Rfy];

    public bool CanHandle(RfxComPacket packet) => packet.PacketType == PacketTypes.Rfy;

    public Task<IEnumerable<MqttMessage>> HandleAsync(RfxComPacket packet)
    {
        var messages = new List<MqttMessage>();

        try
        {
            if (packet.Data.Length < 5)
            {
                _logger.LogWarning("Paquet RFY trop court ({Len} octets)", packet.Data.Length);
                return Task.FromResult<IEnumerable<MqttMessage>>(messages);
            }

            var d = packet.Data;
            var id = $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}";
            var unitCode = d[3];
            var command = d[4];
            var commandName = DecodeCommand(command);

            // Chercher le device par son ID dans la config
            var deviceName = FindDeviceName(d[0], d[1], d[2], unitCode) ?? id;

            var topic = _topics.Event(MqttTopics.KindSomfy, deviceName);
            var nowUtc = DateTime.UtcNow;
            var payload = JsonSerializer.Serialize(new
            {
                id,
                unitCode,
                command = commandName,
                receivedAt = nowUtc,
                receivedAtFormatted = nowUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            });

            messages.Add(new MqttMessage(topic, payload));

            _logger.LogInformation("Somfy RFY [{Id}] unit={Unit} → {Command}",
                id, unitCode, commandName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur parsing paquet RFY : {Hex}",
                RfxComProtocol.FormatHex(packet.RawBytes));
        }

        return Task.FromResult<IEnumerable<MqttMessage>>(messages);
    }

    #endregion

    #region TX - Envoi de commandes (MQTT → série)

    /// <summary>
    /// Traite une commande MQTT reçue pour un volet Somfy.
    /// </summary>
    /// <param name="deviceName">Nom du device (extrait du topic)</param>
    /// <param name="payload">Payload JSON : { "command": "up|down|stop|program" }</param>
    public async Task HandleCommandAsync(string deviceName, string payload, CancellationToken ct)
    {
        // Chercher le device dans la config
        var device = _devices.Snapshot.Somfy
            .FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            _logger.LogWarning("Volet Somfy '{Name}' non trouvé dans la configuration", deviceName);
            return;
        }

        // Parser la commande
        SomfyCommandPayload? cmd;
        try
        {
            cmd = JsonSerializer.Deserialize<SomfyCommandPayload>(payload, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Payload Somfy invalide : {Payload}", payload);
            return;
        }

        if (cmd is null)
        {
            _logger.LogWarning("Payload Somfy vide");
            return;
        }

        var rfyCommand = cmd.ToRfyCommand();
        if (rfyCommand is null)
        {
            _logger.LogWarning("Commande Somfy inconnue : '{Command}'", cmd.Command);
            return;
        }

        // Construire et envoyer la trame RFY
        try
        {
            var idBytes = device.GetIdBytes();   // mis en cache sur SomfyDevice
            var frame = _protocol.BuildSomfyCommand(device.SubType, idBytes, device.UnitCode, rfyCommand.Value);

            _logger.LogInformation("Somfy TX → {Name} [{Id}] unit={Unit} : {Command}",
                device.Name,
                device.Id,
                device.UnitCode,
                cmd.Command.ToUpperInvariant());

            await _serialService.SendRawAsync(frame, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur envoi commande Somfy vers {Name}", device.Name);
        }
    }

    #endregion

    #region Helpers

    private string? FindDeviceName(byte id1, byte id2, byte id3, byte unitCode)
    {
        foreach (var device in _devices.Snapshot.Somfy)
        {
            try
            {
                var idBytes = device.GetIdBytes();   // mis en cache sur SomfyDevice
                if (idBytes[0] == id1 && idBytes[1] == id2 && idBytes[2] == id3
                    && device.UnitCode == unitCode)
                {
                    return device.Name;
                }
            }
            catch { /* Config invalide, on passe */ }
        }
        return null;
    }

    private static string DecodeCommand(byte command) => command switch
    {
        SomfyCommands.Stop => "stop",
        SomfyCommands.Up => "up",
        SomfyCommands.Down => "down",
        SomfyCommands.Program => "program",
        SomfyCommands.UpStop => "up_stop",
        SomfyCommands.DownStop => "down_stop",
        _ => $"unknown_0x{command:X2}"
    };

    #endregion
}
