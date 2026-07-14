using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using Rfx2Mqtt.Configuration;

namespace Rfx2Mqtt.Mqtt;

/// <summary>
/// <b>EN:</b> MQTT communication service (MQTTnet v5.x). Handles broker connection, sensor data
/// publication and subscription to command topics (Somfy, etc.).
/// <para>Reconnection: handled internally by <see cref="HandleDisconnectedAsync"/>. After every
/// successful <see cref="ConnectAsync"/>, command topics are (re)subscribed — required because
/// MQTTnet does not restore subscriptions automatically.</para>
/// <br/>
/// <b>FR:</b> Service de communication MQTT (MQTTnet v5.x). Gère la connexion au broker, la
/// publication des données des capteurs et la souscription aux commandes (Somfy, etc.).
/// <para>Reconnexion : assurée par le handler interne <see cref="HandleDisconnectedAsync"/>.
/// Après chaque <see cref="ConnectAsync"/> réussi, on (re)souscrit aux topics de commande —
/// indispensable car MQTTnet ne restaure pas les souscriptions automatiquement.</para>
/// </summary>
public class MqttService : IDisposable
{
    private readonly ILogger<MqttService> _logger;
    private readonly MqttOptions _options;
    private readonly MqttTopics _topics;

    private IMqttClient? _mqttClient;
    private MqttClientOptions? _builtOptions;
    private CancellationToken _stoppingToken;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private Task? _pingTask;

    /// <summary>Événement déclenché à la réception d'une commande MQTT</summary>
    public event Func<string, string, Task>? CommandReceived;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    /// <summary>
    /// <b>EN:</b> UTC timestamp of the last successful PINGREQ/PINGRESP round-trip
    /// (null until the first ping). Exposed for the /healthz endpoint.<br/>
    /// <b>FR:</b> Horodatage UTC du dernier aller-retour PINGREQ/PINGRESP réussi
    /// (null avant le premier ping). Exposé pour l'endpoint /healthz.
    /// </summary>
    public DateTime? LastPingOkUtc { get; private set; }

    public MqttService(
        ILogger<MqttService> logger,
        IOptions<MqttOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _topics = new MqttTopics(_options.TopicPrefix);
    }

    #region Connexion

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _stoppingToken = cancellationToken;

        // Évite deux ConnectAsync concurrents (Worker + handler DisconnectedAsync).
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_mqttClient is { IsConnected: true })
            {
                _logger.LogDebug("ConnectAsync : déjà connecté, skip");
                return;
            }

            _logger.LogInformation("Connexion au broker MQTT {Host}:{Port}", _options.Host, _options.Port);

            // Première initialisation du client (réutilisé sur les reconnexions)
            if (_mqttClient is null)
            {
                var factory = new MqttClientFactory();
                _mqttClient = factory.CreateMqttClient();
                _mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
                _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
            }

            _builtOptions ??= BuildClientOptions();

            var connectResult = await _mqttClient.ConnectAsync(_builtOptions, cancellationToken);

            if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                throw new Exception($"Échec connexion MQTT: {connectResult.ResultCode}");

            _logger.LogInformation("Connecté au broker MQTT");

            // Status online (format JSON comme zigbee2mqtt)
            await PublishAsync(_topics.BridgeAvailability, MqttTopics.PayloadOnline, retain: true,
                cancellationToken: cancellationToken);

            // (Re-)souscrire aux commandes — obligatoire à chaque reconnexion
            await SubscribeToCommandsAsync(cancellationToken);

            // Démarrer (une seule fois) la surveillance active de la connexion
            _pingTask ??= Task.Run(() => PingLoopAsync(_stoppingToken), CancellationToken.None);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// <b>EN:</b> Active keep-alive probe: sends a PINGREQ every <c>PingIntervalSec</c>. If the
    /// broker does not answer within <c>PingTimeoutSec</c>, the connection is considered zombie
    /// (TCP socket alive but MQTT flow dead) and a reconnection is forced. Complements the
    /// protocol keep-alive, which only fires when the client sends nothing — a bridge that
    /// publishes into a black hole never triggers it.<br/>
    /// <b>FR:</b> Sonde keep-alive active : envoie un PINGREQ toutes les <c>PingIntervalSec</c>.
    /// Si le broker ne répond pas sous <c>PingTimeoutSec</c>, la connexion est considérée zombie
    /// (socket TCP vivant mais flux MQTT mort) et une reconnexion est forcée. Complète le
    /// keep-alive protocolaire, qui ne se déclenche que si le client n'émet rien — un bridge qui
    /// publie dans le vide ne l'active jamais.
    /// </summary>
    private async Task PingLoopAsync(CancellationToken stoppingToken)
    {
        if (_options.PingIntervalSec <= 0)
        {
            _logger.LogInformation("Ping MQTT actif désactivé (PingIntervalSec=0)");
            return;
        }

        _logger.LogInformation("Ping MQTT actif démarré : intervalle {Interval}s, timeout {Timeout}s",
            _options.PingIntervalSec, _options.PingTimeoutSec);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PingIntervalSec));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Déconnexion déjà connue → la reconnexion est gérée par HandleDisconnectedAsync
                if (_mqttClient is null || !_mqttClient.IsConnected)
                    continue;

                try
                {
                    using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    pingCts.CancelAfter(TimeSpan.FromSeconds(_options.PingTimeoutSec));
                    await _mqttClient.PingAsync(pingCts.Token);
                    LastPingOkUtc = DateTime.UtcNow;
                    _logger.LogDebug("Ping MQTT OK");
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Ping MQTT sans réponse après {Timeout}s → connexion zombie, reconnexion forcée",
                        _options.PingTimeoutSec);
                    await ForceReconnectAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt du service — normal
        }
    }

    /// <summary>
    /// <b>EN:</b> Tears down the current (presumed dead) connection then reconnects. The
    /// disconnect is best-effort with a short timeout — on a dead socket it can hang or throw.<br/>
    /// <b>FR:</b> Démonte la connexion courante (présumée morte) puis se reconnecte. La
    /// déconnexion est best-effort avec un timeout court — sur un socket mort elle peut
    /// bloquer ou lever.
    /// </summary>
    private async Task ForceReconnectAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await _mqttClient!.DisconnectAsync(new MqttClientDisconnectOptions(), cts.Token);
        }
        catch { /* socket mort : l'échec du disconnect est attendu */ }

        try
        {
            // ConnectAsync est idempotent (skip si déjà reconnecté par HandleDisconnectedAsync)
            await ConnectAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Échec de reconnexion après ping mort (nouvelle tentative au prochain ping)");
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            // Last Will Testament : si le bridge crash, le broker publie offline automatiquement
            .WithWillTopic(_topics.BridgeAvailability)
            .WithWillPayload(Encoding.UTF8.GetBytes(MqttTopics.PayloadOffline))
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true);

        if (!string.IsNullOrEmpty(_options.Username))
            builder.WithCredentials(_options.Username, _options.Password ?? "");

        return builder.Build();
    }

    private async Task SubscribeToCommandsAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient is null) return;

        await _mqttClient.SubscribeAsync(
            new MqttTopicFilterBuilder()
                .WithTopic(_topics.CommandWildcard)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            cancellationToken);

        _logger.LogInformation("Souscrit à {Topic}", _topics.CommandWildcard);
    }

    private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (_stoppingToken.IsCancellationRequested) return;

        _logger.LogWarning("Déconnecté du broker MQTT : {Reason}. Reconnexion dans {Delay}s...",
            e.Reason, _options.ReconnectDelaySec);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySec), _stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        try
        {
            // ConnectAsync se charge de re-souscrire aux commandes
            await ConnectAsync(_stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de reconnexion MQTT (nouvelle tentative au prochain disconnect)");
        }
    }

    private async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        _logger.LogDebug("MQTT RX: {Topic} = {Payload}", topic, payload);

        if (CommandReceived is not null)
            await CommandReceived.Invoke(topic, payload);
    }

    #endregion

    #region Publication

    /// <summary>Publie un message sur un topic. Retourne sans erreur si le client est déconnecté.</summary>
    public async Task PublishAsync(
        string topic,
        string payload,
        bool retain = false,
        MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce,
        CancellationToken cancellationToken = default)
    {
        if (_mqttClient is null || !_mqttClient.IsConnected)
        {
            _logger.LogWarning("Tentative de publication MQTT alors que le client n'est pas connecté ({Topic})", topic);
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(retain)
            .Build();

        await _mqttClient.PublishAsync(message, cancellationToken);
        _logger.LogDebug("MQTT TX: {Topic} = {Payload}", topic, payload);
    }

    #endregion

    #region Déconnexion

    public async Task DisconnectAsync()
    {
        if (_mqttClient?.IsConnected == true)
        {
            // Désactive le handler de reconnexion avant la déconnexion volontaire
            _mqttClient.DisconnectedAsync -= HandleDisconnectedAsync;

            try
            {
                // Publier status offline avant déconnexion propre.
                // Guard against ObjectDisposedException if the host is already tearing down.
                await PublishAsync(_topics.BridgeAvailability, MqttTopics.PayloadOffline, retain: true);
                await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions());
            }
            catch (ObjectDisposedException) { /* client déjà disposé lors de l'arrêt — ignoré */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la déconnexion MQTT propre");
            }
        }
    }

    public void Dispose()
    {
        _mqttClient?.Dispose();
        _connectLock.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}
