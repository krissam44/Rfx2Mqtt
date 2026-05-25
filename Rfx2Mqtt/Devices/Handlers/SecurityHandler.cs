using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Discovery;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;

namespace Rfx2Mqtt.Devices.Handlers;

/// <summary>
/// <b>EN:</b> Handler for Security1 sensors (packet 0x20). Covers X10 motion detectors,
/// door/window contacts, smoke detectors, etc.
/// <para>Auto-reset motion: if <c>MotionClearDelaySec &gt; 0</c>, a timer is armed on every motion
/// detection. On expiry, motion/occupancy are reset to false if no new frame arrived — required
/// for X10 sensors that never emit an explicit <c>no_motion</c> frame.</para>
/// <br/>
/// <b>FR:</b> Handler pour les capteurs Security1 (paquet 0x20). Gère les détecteurs de mouvement
/// X10, contacts porte/fenêtre, détecteurs de fumée, etc.
/// <para>Auto-reset mouvement : si <c>MotionClearDelaySec &gt; 0</c>, un timer est armé à chaque
/// détection. À expiration, motion/occupancy sont remis à false si aucune nouvelle trame
/// n'est arrivée — indispensable pour les capteurs X10 qui n'envoient jamais de
/// <c>no_motion</c> explicite.</para>
///
/// <para><b>EN/FR:</b> Frame layout 0x20 (after header) / Format trame 0x20 (après header) :</para>
/// <list type="bullet">
///   <item>Data[0..2] = sensor ID (3 bytes / 3 octets)</item>
///   <item>Data[3] = status (bit7 = tamper, bits 0-6 = state / état)</item>
///   <item>Data[4] = battery (high nibble) + signal (low nibble)</item>
/// </list>
/// </summary>
public class SecurityHandler : IPacketHandler, IDisposable
{
    private readonly ILogger<SecurityHandler> _logger;
    private readonly MqttTopics _topics;
    private readonly IDeviceRepository _devices;
    private readonly PermitJoinState _permitJoin;
    private readonly HomeAssistantDiscoveryService _discovery;
    private readonly AvailabilityService _availability;
    private readonly MqttService _mqtt;
    private readonly int _motionClearDelaySec;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Type de paquet Security1
    private const byte PacketTypeSecurity1 = 0x20;

    // Timer d'auto-reset par capteur (clé = sensorId)
    private readonly Dictionary<string, CancellationTokenSource> _motionTimers = new();
    // Dernière donnée connue par capteur pour reconstituer le JSON de clear
    private readonly Dictionary<string, SecurityData> _lastData = new();
    private readonly Lock _timersLock = new();

    public SecurityHandler(
        ILogger<SecurityHandler> logger,
        MqttTopics topics,
        IDeviceRepository devices,
        IOptions<RfxComOptions> rfxOptions,
        PermitJoinState permitJoin,
        HomeAssistantDiscoveryService discovery,
        AvailabilityService availability,
        MqttService mqtt)
    {
        _logger = logger;
        _topics = topics;
        _devices = devices;
        _permitJoin = permitJoin;
        // MotionClearDelaySec reste dans RfxComOptions (config technique, pas un device)
        _motionClearDelaySec = rfxOptions.Value.MotionClearDelaySec;
        _discovery = discovery;
        _availability = availability;
        _mqtt = mqtt;
    }

    public IReadOnlyList<byte> SupportedPacketTypes { get; } = [PacketTypeSecurity1];

    public bool CanHandle(RfxComPacket packet) => packet.PacketType == PacketTypeSecurity1;

    public async Task<IEnumerable<MqttMessage>> HandleAsync(RfxComPacket packet)
    {
        var messages = new List<MqttMessage>();

        try
        {
            if (packet.Data.Length < 5)
            {
                _logger.LogWarning("Paquet Security1 trop court ({Len} octets data, 5 attendus)", packet.Data.Length);
                return messages;
            }

            var d = packet.Data;
            var sensorId = $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}";
            var statusRaw = d[3];
            var tamper = (statusRaw & SecurityStatus.TamperFlag) != 0;
            var baseStatus = (byte)(statusRaw & 0x7F);
            var motion = baseStatus == SecurityStatus.Motion
                      || baseStatus == SecurityStatus.Alarm
                      || baseStatus == SecurityStatus.AlarmDelayed;
            var battery = (d[4] >> 4) & 0x0F;
            var signal = d[4] & 0x0F;

            var data = new SecurityData
            {
                SensorType = $"0x20/{packet.SubType:X2}",
                SensorModel = Security1SubTypes.GetModelName(packet.SubType),
                SensorId = sensorId,
                StatusRaw = statusRaw,
                Status = SecurityStatus.Decode(statusRaw),
                Motion = motion,
                Tamper = tamper,
                BatteryLevel = battery,
                SignalLevel = signal,
                ReceivedAt = packet.ReceivedAt
            };

            // Chercher le device dans la config (nom friendly)
            var configured = _devices.Snapshot.Security
                .FirstOrDefault(d => d.Id.Equals(sensorId, StringComparison.OrdinalIgnoreCase));

            // PermitJoin : si false et device non configuré → ignorer
            if (!_permitJoin.IsEnabled && configured is null)
            {
                _logger.LogDebug("Capteur Security [{Id}] ignoré (PermitJoin=false, non configuré)", sensorId);
                return messages;
            }

            var topicName = configured?.Name ?? sensorId;
            const string kind = MqttTopics.KindSecurity;

            // Auto-discovery Home Assistant (une seule fois par capteur)
            await _discovery.PublishSecurityDiscoveryAsync(data, topicName);

            // Topic : rfxcom/sensor/security/{topicName}
            var topicBase = _topics.SensorBase(kind, topicName);

            // Signaler le capteur comme actif (availability)
            await _availability.ReportSeenAsync(
                $"{kind}/{topicName}",
                _topics.SensorAvailability(kind, topicName));

            // JSON complet
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            messages.Add(new MqttMessage(topicBase, json, Retain: true));

            // Valeurs individuelles
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrStatus),
                data.Status, Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrMotion),
                motion ? "ON" : "OFF", Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrTamper),
                tamper ? "ON" : "OFF", Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrBattery),
                BatteryLabel(battery), Retain: true));
            messages.Add(new MqttMessage(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrSignal),
                signal.ToString(CultureInfo.InvariantCulture), Retain: true));

            // Info : transition motion détectée. Le reste en Debug pour éviter de spammer.
            var logLevel = motion ? LogLevel.Information : LogLevel.Debug;
            _logger.Log(logLevel,
                "Security {Model} [{Id}]{Name} : {Status} motion={Motion} tamper={Tamper} (signal:{Sig} bat:{Bat})",
                data.SensorModel, sensorId,
                configured is not null ? $" \"{configured.Name}\"" : "",
                data.Status,
                motion ? "OUI" : "non",
                tamper ? "OUI" : "non",
                signal,
                BatteryLabel(battery).ToUpperInvariant());

            // Auto-reset : armer (ou réarmer) le timer si le capteur vient de détecter du mouvement
            if (motion && _motionClearDelaySec > 0)
            {
                ArmMotionClearTimer(sensorId, topicName, data);
            }
            else if (!motion)
            {
                // Trame "no_motion" reçue explicitement → annuler le timer
                CancelMotionTimer(sensorId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur parsing paquet Security1 : {Hex}",
                RfxComProtocol.FormatHex(packet.RawBytes));
        }

        return messages;
    }

    /// <summary>
    /// Seuil batterie RFXCom : niveau 0–3 = low, 4–9 = ok.
    /// </summary>
    private static string BatteryLabel(int level) => level <= 3 ? "low" : "ok";

    /// <summary>
    /// Arme (ou réarme) le timer d'auto-reset pour un capteur.
    /// Tout timer précédent est annulé avant d'en démarrer un nouveau.
    /// </summary>
    private void ArmMotionClearTimer(string sensorId, string topicName, SecurityData lastData)
    {
        CancellationTokenSource cts;

        lock (_timersLock)
        {
            CancelMotionTimerLocked(sensorId);
            cts = new CancellationTokenSource();
            _motionTimers[sensorId] = cts;
            _lastData[sensorId] = lastData;
        }

        var delay = TimeSpan.FromSeconds(_motionClearDelaySec);
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                await PublishMotionClearAsync(sensorId, topicName);
            }
            catch (OperationCanceledException)
            {
                // Timer annulé par une nouvelle détection ou l'arrêt du service — normal
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur auto-reset mouvement [{Id}]", sensorId);
            }
        }, token);

        _logger.LogDebug("Timer auto-reset mouvement [{Id}] armé pour {Delay}s", sensorId, _motionClearDelaySec);
    }

    /// <summary>
    /// Publie les topics "no motion" pour un capteur dont le timer a expiré.
    /// </summary>
    private async Task PublishMotionClearAsync(string sensorId, string topicName)
    {
        SecurityData? last;
        lock (_timersLock)
        {
            _lastData.TryGetValue(sensorId, out last);
            _motionTimers.Remove(sensorId);
        }

        _logger.LogInformation("Auto-reset mouvement [{Id}] → no_motion (délai {Delay}s écoulé)",
            sensorId, _motionClearDelaySec);

        const string kind = MqttTopics.KindSecurity;
        var topicBase = _topics.SensorBase(kind, topicName);

        // Construire un objet "clear" basé sur la dernière donnée connue
        var cleared = new SecurityData
        {
            SensorType    = last?.SensorType ?? "",
            SensorModel   = last?.SensorModel ?? "",
            SensorId      = sensorId,
            StatusRaw     = SecurityStatus.NoMotion,
            Status        = "no_motion",
            Motion        = false,
            Tamper        = last?.Tamper ?? false,
            BatteryLevel  = last?.BatteryLevel ?? 0,
            SignalLevel   = last?.SignalLevel ?? 0,
            ReceivedAt    = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(cleared, _jsonOptions);
        await _mqtt.PublishAsync(topicBase, json, retain: true);
        await _mqtt.PublishAsync(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrMotion),
            "OFF", retain: true);
        await _mqtt.PublishAsync(_topics.SensorAttribute(kind, topicName, MqttTopics.AttrStatus),
            "no_motion", retain: true);
    }

    private void CancelMotionTimer(string sensorId)
    {
        lock (_timersLock) { CancelMotionTimerLocked(sensorId); }
    }

    private void CancelMotionTimerLocked(string sensorId)
    {
        if (_motionTimers.TryGetValue(sensorId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
            _motionTimers.Remove(sensorId);
        }
    }

    public void Dispose()
    {
        lock (_timersLock)
        {
            foreach (var cts in _motionTimers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _motionTimers.Clear();
        }
    }
}
