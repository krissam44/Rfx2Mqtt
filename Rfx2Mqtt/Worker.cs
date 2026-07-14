using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Handlers;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Discovery;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;
using Rfx2Mqtt.UI;

namespace Rfx2Mqtt;

/// <summary>
/// <b>EN:</b> Main Worker Service — the orchestrator of the bridge.
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Maintain the RFXCom serial and MQTT broker connections (with retry).</item>
///   <item>Subscribe to <see cref="RfxComSerialService.PacketReceived"/> and dispatch each frame
///         to the first matching <see cref="IPacketHandler"/>.</item>
///   <item>Subscribe to <see cref="MqttService.CommandReceived"/> and route MQTT commands
///         (Somfy / Chacon / restart / permit_join) to the proper handler.</item>
///   <item>Publish startup discovery for Home Assistant and the current PermitJoin state.</item>
/// </list>
/// <br/>
/// <b>FR:</b> Worker Service principal — orchestrateur du bridge.
/// <para>Responsabilités :</para>
/// <list type="bullet">
///   <item>Maintenir les connexions série RFXCom et broker MQTT (avec retry).</item>
///   <item>S'abonner à <see cref="RfxComSerialService.PacketReceived"/> et dispatcher chaque trame
///         vers le premier <see cref="IPacketHandler"/> qui matche.</item>
///   <item>S'abonner à <see cref="MqttService.CommandReceived"/> et router les commandes MQTT
///         (Somfy / Chacon / restart / permit_join) vers le bon handler.</item>
///   <item>Publier le discovery initial pour Home Assistant et l'état courant de PermitJoin.</item>
/// </list>
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly RfxComSerialService _serialService;
    private readonly MqttService _mqttService;
    private readonly MqttTopics _topics;
    private readonly RfxComOptions _rfxOptions;
    private readonly PermitJoinState _permitJoin;
    private readonly IEnumerable<IPacketHandler> _handlers;
    private readonly SomfyRtsHandler _somfyHandler;
    private readonly Lighting2Handler _chaconHandler;
    private readonly HomeAssistantDiscoveryService _discovery;
    private readonly MatterBridgePublisher _matterBridge;
    private readonly AvailabilityService _availability;
    private readonly UiEventService _uiEvents;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationToken _stoppingToken;

    public Worker(
        ILogger<Worker> logger,
        RfxComSerialService serialService,
        MqttService mqttService,
        MqttTopics topics,
        IOptions<RfxComOptions> rfxOptions,
        PermitJoinState permitJoin,
        IEnumerable<IPacketHandler> handlers,
        SomfyRtsHandler somfyHandler,
        Lighting2Handler chaconHandler,
        HomeAssistantDiscoveryService discovery,
        MatterBridgePublisher matterBridge,
        AvailabilityService availability,
        UiEventService uiEvents,
        IDeviceRepository deviceRepository,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _serialService = serialService;
        _mqttService = mqttService;
        _topics = topics;
        _rfxOptions = rfxOptions.Value;
        _permitJoin = permitJoin;
        _handlers = handlers;
        _somfyHandler = somfyHandler;
        _chaconHandler = chaconHandler;
        _discovery = discovery;
        _matterBridge = matterBridge;
        _availability = availability;
        _uiEvents = uiEvents;
        _deviceRepository = deviceRepository;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        _logger.LogInformation("========================================");
        _logger.LogInformation("  Rfx2Mqtt - RFXCom to MQTT Bridge");
        _logger.LogInformation("  Démarrage du service...");
        _logger.LogInformation("========================================");

        // Connecter le handler de paquets série → MQTT
        _serialService.PacketReceived += OnPacketReceived;

        // Connecter le handler de commandes MQTT → série
        _mqttService.CommandReceived += OnMqttCommandReceived;

        // Boucle principale avec reconnexion automatique
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Connexion MQTT (sa propre logique gère le retry et la re-souscription)
                if (!_mqttService.IsConnected)
                {
                    _logger.LogInformation("Connexion au broker MQTT...");
                    await _mqttService.ConnectAsync(stoppingToken);
                }

                // 2. Connexion série et initialisation RFXCom
                if (!_serialService.IsConnected)
                {
                    _logger.LogInformation("Connexion au RFXCom sur {Port}...", _rfxOptions.PortName);
                    await _serialService.ConnectAndInitializeAsync(stoppingToken);
                }

                _logger.LogInformation("Service opérationnel - En écoute des trames RFXCom...");
                _logger.LogInformation("PermitJoin = {State}",
                    _permitJoin.IsEnabled ? "ACTIVÉ (toutes les sondes)" : "DÉSACTIVÉ (sondes configurées uniquement)");

                // Publier l'état PermitJoin sur MQTT
                await PublishPermitJoinStateAsync(stoppingToken);

                // Publier le discovery HA pour les équipements configurés
                await _discovery.PublishStartupDiscoveryAsync();

                // Publier l'inventaire Matter Bridge
                await _matterBridge.PublishAsync();

                // Démarrer le suivi de disponibilité des capteurs
                _availability.Start();

                // 3. Boucle de surveillance — on ne pilote QUE le serial ici.
                // MqttService gère sa propre reconnexion en interne (et resouscrit).
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!_serialService.IsConnected)
                    {
                        _logger.LogWarning("Connexion série perdue !");
                        break;
                    }

                    await Task.Delay(5000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur dans la boucle principale");

                _serialService.Disconnect();

                _logger.LogInformation("Nouvelle tentative dans {Delay}s...", _rfxOptions.ReconnectDelaySec);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_rfxOptions.ReconnectDelaySec), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        // Arrêt propre
        _logger.LogInformation("Arrêt du service...");
        await _availability.SetAllOfflineAsync();
        await _mqttService.DisconnectAsync();
        _serialService.Disconnect();
        _availability.Dispose();
        _logger.LogInformation("Service arrêté.");
    }

    private Task PublishPermitJoinStateAsync(CancellationToken cancellationToken)
        => _mqttService.PublishAsync(
            _topics.PermitJoinState,
            $"{{\"state\":\"{(_permitJoin.IsEnabled ? "true" : "false")}\"}}",
            retain: true,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Traitement d'un paquet reçu du RFXCom.
    /// Dispatch vers les handlers enregistrés et publication MQTT.
    /// </summary>
    private async void OnPacketReceived(object? sender, PacketReceivedEventArgs e)
    {
        try
        {
            var packet = e.Packet;
            bool handled = false;

            foreach (var handler in _handlers)
            {
                if (handler.CanHandle(packet))
                {
                    var messages = await handler.HandleAsync(packet);
                    foreach (var msg in messages)
                    {
                        await _mqttService.PublishAsync(msg.Topic, msg.Payload, msg.Retain,
                            cancellationToken: _stoppingToken);
                    }
                    handled = true;
                    break;   // un seul handler par paquet — évite la duplication accidentelle
                }
            }

            if (!handled && packet.PacketType != Devices.Models.PacketTypes.InterfaceMessage)
            {
                _logger.LogDebug("Paquet non géré : {Packet}", packet);
            }

            // Alimenter le ring buffer UI (page Statut + page Découverte)
            _uiEvents.Add(BuildUiEvent(packet));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du traitement du paquet {Packet}", e.Packet);
        }
    }

    /// <summary>
    /// Construit un RfxEvent lisible pour l'UI à partir d'un paquet brut.
    /// Résout également le nom amical configuré depuis <see cref="IDeviceRepository"/>.
    /// </summary>
    private RfxEvent BuildUiEvent(RfxComPacket packet)
    {
        var d = packet.Data;
        var (packetTypeName, deviceId, summary) = packet.PacketType switch
        {
            PacketTypes.TempHumidity or PacketTypes.TempHumBaro
                when d.Length >= 7 =>
                ("TH/THB",
                 $"0x{d[0]:X2}{d[1]:X2}",
                 $"{((d[2] & 0x7F) * 256 + d[3]) / 10.0:F1}°C {d[4]}%"),

            PacketTypes.Security1
                when d.Length >= 5 =>
                ("Security",
                 $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}",
                 $"status=0x{(d[3] & 0x7F):X2}"),

            PacketTypes.Lighting2
                when d.Length >= 6 =>
                ("Chacon",
                 $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}{d[3]:X2}",
                 $"unit={d[4]} cmd={d[5]}"),

            PacketTypes.Rfy
                when d.Length >= 5 =>
                ("Somfy",
                 $"0x{d[0]:X2}{d[1]:X2}{d[2]:X2}",
                 $"unit={d[3]} cmd=0x{d[4]:X2}"),

            _ => ($"0x{packet.PacketType:X2}", "—", $"{d.Length} octets")
        };

        // Résolution du nom amical depuis l'inventaire des appareils
        var devices = _deviceRepository.Snapshot;
        string? deviceName = packet.PacketType switch
        {
            PacketTypes.TempHumidity or PacketTypes.TempHumBaro when d.Length >= 2 =>
                devices.Oregon.FirstOrDefault(o =>
                    string.Equals(o.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.Name,

            PacketTypes.Security1 when d.Length >= 3 =>
                devices.Security.FirstOrDefault(s =>
                    string.Equals(s.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.Name,

            PacketTypes.Rfy when d.Length >= 3 =>
                devices.Somfy.FirstOrDefault(s =>
                {
                    try { var b = s.GetIdBytes(); return b[0] == d[0] && b[1] == d[1] && b[2] == d[2]; }
                    catch { return false; }
                })?.Name,

            PacketTypes.Lighting2 when d.Length >= 4 =>
                devices.Chacon.FirstOrDefault(c =>
                {
                    try { var b = c.GetIdBytes(); return b[0] == d[0] && b[1] == d[1] && b[2] == d[2] && b[3] == d[3]; }
                    catch { return false; }
                })?.Name,

            _ => null
        };

        return new RfxEvent(packet.ReceivedAt, packetTypeName, deviceId, deviceName, summary);
    }

    /// <summary>
    /// Traitement d'une commande reçue via MQTT.
    /// Dispatch vers le handler approprié selon le topic.
    ///
    /// Topics supportés :
    ///   {prefix}/command/somfy/{name}     → commande volet Somfy
    ///   {prefix}/command/chacon/{name}    → commande switch Chacon
    ///   {prefix}/command/restart          → redémarrage du service
    ///   {prefix}/command/permit_join      → payload "true"/"false"
    /// </summary>
    private async Task OnMqttCommandReceived(string topic, string payload)
    {
        _logger.LogInformation("Commande MQTT reçue : {Topic} = {Payload}", topic, payload);

        try
        {
            var prefix = $"{_topics.Prefix}/command/";
            if (!topic.StartsWith(prefix))
                return;

            var remainder = topic[prefix.Length..];

            // Commandes sans sous-topic (restart, permit_join)
            if (!remainder.Contains('/'))
            {
                switch (remainder.ToLowerInvariant())
                {
                    case "restart":
                        _logger.LogWarning("Commande RESTART reçue → arrêt du service...");
                        await _mqttService.PublishAsync(
                            $"{prefix}restart/response",
                            "{\"status\":\"restarting\"}", retain: false,
                            cancellationToken: _stoppingToken);
                        _lifetime.StopApplication();
                        break;

                    case "permit_join":
                        var enable = payload.Trim().Trim('"').ToLowerInvariant() is "true" or "1" or "on";
                        var changed = await _permitJoin.SetAsync(enable, _stoppingToken);
                        if (changed)
                        {
                            _logger.LogInformation("PermitJoin → {State}", enable ? "ACTIVÉ" : "DÉSACTIVÉ");
                            await PublishPermitJoinStateAsync(_stoppingToken);
                        }
                        break;

                    default:
                        _logger.LogWarning("Commande inconnue : {Cmd}", remainder);
                        break;
                }
                return;
            }

            // Commandes avec device : {type}/{device_name}
            var slashIndex = remainder.IndexOf('/');
            var commandType = remainder[..slashIndex];
            var deviceName = remainder[(slashIndex + 1)..];

            switch (commandType.ToLowerInvariant())
            {
                case "somfy":
                    await _somfyHandler.HandleCommandAsync(deviceName, payload, _stoppingToken);
                    break;

                case "chacon":
                    await _chaconHandler.HandleCommandAsync(deviceName, payload, _stoppingToken);
                    break;

                default:
                    _logger.LogWarning("Type de commande inconnu : {Type}", commandType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur traitement commande MQTT {Topic}", topic);
        }
    }
}
