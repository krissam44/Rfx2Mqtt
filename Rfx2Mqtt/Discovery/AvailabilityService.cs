using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Mqtt;

namespace Rfx2Mqtt.Discovery;

/// <summary>
/// <b>EN:</b> Tracks RFXCom sensor availability.
/// <para>Principle: every sensor has a "last seen" timestamp updated on each frame. A periodic
/// loop (<see cref="PeriodicTimer"/>) checks if the elapsed time exceeds the configured timeout —
/// publishes "offline" on the sensor's availability topic if so, "online" when the sensor
/// resumes emitting.</para>
/// <para>Oregon/Bresser probes emit every 30–90 seconds depending on the model, so a 5-minute
/// (300 s) timeout is a safe default.</para>
/// <para>All timestamps are in UTC to avoid the 1-hour jump on DST transitions (which used to
/// flap online/offline 2× a year).</para>
/// <br/>
/// <b>FR:</b> Suit la disponibilité des capteurs RFXCom.
/// <para>Principe : chaque capteur a un "last seen" mis à jour à chaque trame. Un loop périodique
/// (<see cref="PeriodicTimer"/>) vérifie si le délai dépasse le timeout configuré — publie
/// "offline" sur le topic availability le cas échéant, "online" quand un capteur réémet.</para>
/// <para>Les sondes Oregon/Bresser émettent toutes les 30–90 secondes selon le modèle, un timeout
/// de 5 minutes (300 s) est un bon défaut.</para>
/// <para>Tous les timestamps sont en UTC pour éviter le saut d'1 heure aux changements d'heure
/// été/hiver (qui faisait flapper online/offline 2× par an).</para>
/// </summary>
public class AvailabilityService : IDisposable
{
    private readonly ILogger<AvailabilityService> _logger;
    private readonly MqttService _mqttService;
    private readonly int _timeoutSeconds;
    private readonly int _checkIntervalSeconds;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    /// <summary>Dictionnaire sensorKey → état de disponibilité</summary>
    private readonly ConcurrentDictionary<string, SensorAvailability> _sensors = new();

    public AvailabilityService(
        ILogger<AvailabilityService> logger,
        MqttService mqttService,
        IOptions<RfxComOptions> rfxOptions)
    {
        _logger = logger;
        _mqttService = mqttService;
        _timeoutSeconds = rfxOptions.Value.AvailabilityTimeoutSec;
        _checkIntervalSeconds = rfxOptions.Value.AvailabilityCheckIntervalSec;
    }

    /// <summary>Démarre la boucle de vérification périodique</summary>
    public void Start()
    {
        if (_loopTask is not null) return;

        _logger.LogInformation(
            "Availability démarré : timeout={Timeout}s, vérification toutes les {Interval}s",
            _timeoutSeconds, _checkIntervalSeconds);

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunCheckLoopAsync(_loopCts.Token));
    }

    private async Task RunCheckLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_checkIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await CheckAvailabilityOnceAsync();
                }
                catch (Exception ex)
                {
                    // Ne jamais laisser une exception tuer le loop
                    _logger.LogError(ex, "Erreur dans la vérification de disponibilité");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal
        }
    }

    /// <summary>
    /// Signale qu'un capteur vient d'émettre.
    /// Appelé par les handlers à chaque réception de paquet.
    /// </summary>
    /// <param name="sensorKey">Clé unique du capteur (ex: "th/0xA201")</param>
    /// <param name="availabilityTopic">Topic MQTT d'availability pour ce capteur</param>
    public async Task ReportSeenAsync(string sensorKey, string availabilityTopic)
    {
        var now = DateTime.UtcNow;

        SensorAvailability sensor = _sensors.GetOrAdd(sensorKey,
            _ => new SensorAvailability(sensorKey, availabilityTopic));

        bool shouldPublishOnline;
        lock (sensor.Lock)
        {
            sensor.LastSeen = now;
            if (sensor.LastPublished == DateTime.MinValue)
            {
                // Première fois qu'on voit ce capteur
                sensor.IsOnline = true;
                shouldPublishOnline = true;
            }
            else if (!sensor.IsOnline)
            {
                // De retour après timeout
                sensor.IsOnline = true;
                shouldPublishOnline = true;
                _logger.LogInformation("Capteur {Key} de retour → online", sensorKey);
            }
            else
            {
                shouldPublishOnline = false;
            }
        }

        if (shouldPublishOnline)
            await PublishAvailabilityAsync(sensor, MqttTopics.PayloadOnline);
    }

    /// <summary>Vérification périodique de tous les capteurs</summary>
    private async Task CheckAvailabilityOnceAsync()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(_timeoutSeconds);

        foreach (var kvp in _sensors)
        {
            var sensor = kvp.Value;

            bool shouldPublishOffline;
            TimeSpan elapsed;
            lock (sensor.Lock)
            {
                elapsed = now - sensor.LastSeen;
                if (sensor.IsOnline && elapsed > timeout)
                {
                    sensor.IsOnline = false;
                    shouldPublishOffline = true;
                }
                else
                {
                    shouldPublishOffline = false;
                }
            }

            if (shouldPublishOffline)
            {
                await PublishAvailabilityAsync(sensor, MqttTopics.PayloadOffline);
                _logger.LogWarning("Capteur {Key} inactif depuis {Elapsed:F0}s → offline",
                    sensor.Key, elapsed.TotalSeconds);
            }
        }
    }

    private async Task PublishAvailabilityAsync(SensorAvailability sensor, string status)
    {
        try
        {
            await _mqttService.PublishAsync(sensor.AvailabilityTopic, status, retain: true);
            lock (sensor.Lock)
            {
                sensor.LastPublished = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur publication availability pour {Key}", sensor.Key);
        }
    }

    /// <summary>
    /// Publie "offline" pour tous les capteurs connus.
    /// Appelé à l'arrêt du service.
    /// </summary>
    public async Task SetAllOfflineAsync()
    {
        foreach (var kvp in _sensors)
        {
            var sensor = kvp.Value;
            bool wasOnline;
            lock (sensor.Lock)
            {
                wasOnline = sensor.IsOnline;
                if (wasOnline) sensor.IsOnline = false;
            }
            if (wasOnline)
                await PublishAvailabilityAsync(sensor, MqttTopics.PayloadOffline);
        }
    }

    public void Dispose()
    {
        try
        {
            _loopCts?.Cancel();
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* ignored */ }
        _loopCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// État de disponibilité d'un capteur — instance unique par sensorKey,
/// modifiée sous <see cref="Lock"/> pour cohérence multi-thread.
/// </summary>
internal class SensorAvailability
{
    public string Key { get; }
    public string AvailabilityTopic { get; }
    public DateTime LastSeen { get; set; }
    public DateTime LastPublished { get; set; } = DateTime.MinValue;
    public bool IsOnline { get; set; }
    public Lock Lock { get; } = new();

    public SensorAvailability(string key, string availabilityTopic)
    {
        Key = key;
        AvailabilityTopic = availabilityTopic;
    }
}
