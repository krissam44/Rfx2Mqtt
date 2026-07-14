using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;
using Rfx2Mqtt.UI;

namespace Rfx2Mqtt.Health;

/// <summary>
/// <b>EN:</b> Health check for the <c>/healthz</c> endpoint. Aggregates the three vital signs
/// of the bridge:
/// <list type="bullet">
///   <item>MQTT broker connection (Unhealthy if down)</item>
///   <item>RFXCom serial connection (Unhealthy if down)</item>
///   <item>Age of the last RF frame received (Degraded beyond <c>AvailabilityTimeoutSec</c> —
///         probes emit every 30–90 s, prolonged silence means a deaf dongle or antenna issue)</item>
/// </list>
/// Consumable by Uptime Kuma, Node-RED or a simple <c>curl</c>: HTTP 200 = Healthy/Degraded,
/// HTTP 503 = Unhealthy.<br/>
/// <b>FR:</b> Health check pour l'endpoint <c>/healthz</c>. Agrège les trois signes vitaux
/// du bridge :
/// <list type="bullet">
///   <item>Connexion au broker MQTT (Unhealthy si tombée)</item>
///   <item>Connexion série RFXCom (Unhealthy si tombée)</item>
///   <item>Âge de la dernière trame RF reçue (Degraded au-delà d'<c>AvailabilityTimeoutSec</c> —
///         les sondes émettent toutes les 30–90 s, un silence prolongé signifie un dongle sourd
///         ou un problème d'antenne)</item>
/// </list>
/// Consommable par Uptime Kuma, Node-RED ou un simple <c>curl</c> : HTTP 200 = Healthy/Degraded,
/// HTTP 503 = Unhealthy.
/// </summary>
public class BridgeHealthCheck : IHealthCheck
{
    private readonly MqttService _mqtt;
    private readonly RfxComSerialService _serial;
    private readonly UiEventService _uiEvents;
    private readonly int _frameStaleSeconds;

    public BridgeHealthCheck(
        MqttService mqtt,
        RfxComSerialService serial,
        UiEventService uiEvents,
        IOptions<RfxComOptions> rfxOptions)
    {
        _mqtt = mqtt;
        _serial = serial;
        _uiEvents = uiEvents;
        // Même seuil que l'availability des capteurs : au-delà, plus aucune sonde n'émet → louche
        _frameStaleSeconds = rfxOptions.Value.AvailabilityTimeoutSec;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var mqttConnected = _mqtt.IsConnected;
        var serialConnected = _serial.IsConnected;
        var lastFrame = _uiEvents.LastEventUtc;
        var lastFrameAgeSec = lastFrame is null
            ? (double?)null
            : (DateTime.UtcNow - lastFrame.Value).TotalSeconds;

        var data = new Dictionary<string, object>
        {
            ["mqttConnected"] = mqttConnected,
            ["serialConnected"] = serialConnected,
            ["firmware"] = _serial.FirmwareVersion ?? "?",
            ["lastFrameAgeSec"] = lastFrameAgeSec is null ? -1 : Math.Round(lastFrameAgeSec.Value),
            ["lastPingOkUtc"] = _mqtt.LastPingOkUtc?.ToString("O") ?? "never",
            ["version"] = AppInfo.Version,
        };

        if (!mqttConnected || !serialConnected)
        {
            var broken = (!mqttConnected, !serialConnected) switch
            {
                (true, true) => "MQTT et série déconnectés",
                (true, false) => "MQTT déconnecté",
                _ => "port série RFXCom déconnecté"
            };
            return Task.FromResult(HealthCheckResult.Unhealthy(broken, data: data));
        }

        if (lastFrameAgeSec > _frameStaleSeconds)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Aucune trame RF depuis {lastFrameAgeSec:F0}s (seuil {_frameStaleSeconds}s)", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Bridge opérationnel", data));
    }
}
