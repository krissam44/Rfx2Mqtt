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

    /// <summary>Événement déclenché à la réception d'une commande MQTT</summary>
    public event Func<string, string, Task>? CommandReceived;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

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
        }
        finally
        {
            _connectLock.Release();
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
