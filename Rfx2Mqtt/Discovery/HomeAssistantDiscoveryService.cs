using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;
using Rfx2Mqtt.Mqtt;

namespace Rfx2Mqtt.Discovery;

/// <summary>
/// <b>EN:</b> Publishes Home Assistant MQTT Discovery messages so HA auto-creates entities for
/// every Rfx2Mqtt device.
/// <para>Principle: HA listens on <c>homeassistant/{component}/{node_id}/{object_id}/config</c>.
/// Publishing a config JSON on that topic creates the entity; publishing an empty payload removes
/// it.</para>
/// <para>Reference: <c>https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery</c></para>
/// <br/>
/// <b>FR:</b> Publie les messages MQTT Discovery de Home Assistant pour que HA crée
/// automatiquement les entités correspondant à chaque appareil Rfx2Mqtt.
/// <para>Principe : HA écoute sur <c>homeassistant/{component}/{node_id}/{object_id}/config</c>.
/// Publier un JSON de config sur ce topic crée l'entité ; publier un payload vide la supprime.</para>
/// <para>Référence : <c>https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery</c></para>
/// </summary>
public class HomeAssistantDiscoveryService
{
    private readonly ILogger<HomeAssistantDiscoveryService> _logger;
    private readonly MqttService _mqttService;
    private readonly RfxComOptions _rfxOptions;
    private readonly MqttOptions _mqttOptions;

    // Garde trace des devices déjà publiés pour ne pas spammer
    private readonly HashSet<string> _publishedDevices = [];
    private readonly object _lock = new();

    private const string DiscoveryPrefix = "homeassistant";
    private const string NodeId = "rfx2mqtt";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public HomeAssistantDiscoveryService(
        ILogger<HomeAssistantDiscoveryService> logger,
        MqttService mqttService,
        IOptions<RfxComOptions> rfxOptions,
        IOptions<MqttOptions> mqttOptions)
    {
        _logger = logger;
        _mqttService = mqttService;
        _rfxOptions = rfxOptions.Value;
        _mqttOptions = mqttOptions.Value;
    }

    private string TopicPrefix => _mqttOptions.TopicPrefix;

    #region Bloc "device" commun

    /// <summary>
    /// Construit le bloc "device" pour un capteur.
    /// Ce bloc permet à HA de regrouper les entités d'un même device.
    /// </summary>
    private Dictionary<string, object> BuildDeviceBlock(string id, string name, string model, string via = "rfx2mqtt")
    {
        return new Dictionary<string, object>
        {
            ["identifiers"] = new[] { $"{NodeId}_{id}" },
            ["name"] = name,
            ["model"] = model,
            ["manufacturer"] = "RFXCom",
            ["via_device"] = via
        };
    }

    /// <summary>Bloc device pour le bridge rfx2mqtt lui-même</summary>
    private Dictionary<string, object> BuildBridgeDevice()
    {
        return new Dictionary<string, object>
        {
            ["identifiers"] = new[] { NodeId },
            ["name"] = "RFX2MQTT Bridge",
            ["model"] = "RFXtrx433XL",
            ["manufacturer"] = "RFXCom"
        };
    }

    #endregion

    #region Publication Discovery au démarrage

    /// <summary>
    /// Publie le discovery pour le bridge lui-même (status online/offline)
    /// et pour tous les équipements configurés (Somfy, Chacon).
    /// </summary>
    public async Task PublishStartupDiscoveryAsync()
    {
        _logger.LogInformation("Publication MQTT Discovery pour Home Assistant...");

        // Bridge status (binary_sensor)
        await PublishBridgeDiscoveryAsync();

        // Volets Somfy configurés
        foreach (var somfy in _rfxOptions.SomfyDevices)
        {
            await PublishSomfyDiscoveryAsync(somfy);
        }

        // Switches Chacon configurés
        foreach (var chacon in _rfxOptions.ChaconDevices)
        {
            await PublishChaconDiscoveryAsync(chacon);
        }

        _logger.LogInformation("Discovery publié : bridge + {Somfy} volet(s) + {Chacon} switch(es)",
            _rfxOptions.SomfyDevices.Count, _rfxOptions.ChaconDevices.Count);
    }

    #endregion

    #region Bridge

    private async Task PublishBridgeDiscoveryAsync()
    {
        var config = new Dictionary<string, object>
        {
            ["name"] = "Status",
            ["unique_id"] = $"{NodeId}_status",
            ["state_topic"] = $"{TopicPrefix}/availability",
            ["value_template"] = "{{ value_json.state }}",
            ["payload_on"] = "online",
            ["payload_off"] = "offline",
            ["device_class"] = "connectivity",
            ["device"] = BuildBridgeDevice()
        };

        await PublishDiscoveryAsync("binary_sensor", NodeId, "status", config);
    }

    #endregion

    #region Sondes Température/Humidité (auto-découverte au premier message)

    /// <summary>
    /// Publie le discovery pour une sonde temp/hum détectée.
    /// Appelé par le TempHumidityHandler au premier message reçu d'une sonde.
    /// </summary>
    public async Task PublishTempHumidityDiscoveryAsync(TempHumidityData data, string topicName)
    {
        var id = data.SensorId.Replace("0x", "").ToLower();

        if (!ShouldPublish($"th_{id}")) return;

        var deviceType = data.Barometer.HasValue ? "thb" : "th";
        var stateTopic = $"{TopicPrefix}/sensor/{deviceType}/{topicName}";
        var availabilityTopic = $"{stateTopic}/availability";
        var deviceName = $"Sonde {data.SensorModel} ({topicName})";
        var device = BuildDeviceBlock(id, deviceName, data.SensorModel);

        // Température
        await PublishDiscoveryAsync("sensor", NodeId, $"{id}_temp", new Dictionary<string, object>
        {
            ["name"] = "Température",
            ["unique_id"] = $"{NodeId}_{id}_temp",
            ["state_topic"] = $"{stateTopic}/temperature",
            ["unit_of_measurement"] = "°C",
            ["device_class"] = "temperature",
            ["state_class"] = "measurement",
            ["icon"] = "mdi:thermometer",
            ["device"] = device
        }, availabilityTopic);

        // Humidité
        await PublishDiscoveryAsync("sensor", NodeId, $"{id}_hum", new Dictionary<string, object>
        {
            ["name"] = "Humidité",
            ["unique_id"] = $"{NodeId}_{id}_hum",
            ["state_topic"] = $"{stateTopic}/humidity",
            ["unit_of_measurement"] = "%",
            ["device_class"] = "humidity",
            ["state_class"] = "measurement",
            ["icon"] = "mdi:water-percent",
            ["device"] = device
        }, availabilityTopic);

        // Batterie
        await PublishDiscoveryAsync("binary_sensor", NodeId, $"{id}_battery", new Dictionary<string, object>
        {
            ["name"] = "Batterie",
            ["unique_id"] = $"{NodeId}_{id}_battery",
            ["state_topic"] = $"{stateTopic}/battery",
            ["payload_on"] = "low",
            ["payload_off"] = "ok",
            ["device_class"] = "battery",
            ["entity_category"] = "diagnostic",
            ["device"] = device
        }, availabilityTopic);

        // Signal
        await PublishDiscoveryAsync("sensor", NodeId, $"{id}_signal", new Dictionary<string, object>
        {
            ["name"] = "Signal",
            ["unique_id"] = $"{NodeId}_{id}_signal",
            ["state_topic"] = $"{stateTopic}/signal",
            ["icon"] = "mdi:signal",
            ["entity_category"] = "diagnostic",
            ["device"] = device
        }, availabilityTopic);

        // Baromètre (si disponible)
        if (data.Barometer.HasValue)
        {
            await PublishDiscoveryAsync("sensor", NodeId, $"{id}_baro", new Dictionary<string, object>
            {
                ["name"] = "Pression",
                ["unique_id"] = $"{NodeId}_{id}_baro",
                ["state_topic"] = $"{stateTopic}/barometer",
                ["unit_of_measurement"] = "hPa",
                ["device_class"] = "atmospheric_pressure",
                ["state_class"] = "measurement",
                ["icon"] = "mdi:gauge",
                ["device"] = device
            }, availabilityTopic);
        }

        _logger.LogInformation("Discovery publié : sonde {Model} [{Id}]", data.SensorModel, data.SensorId);
    }

    #endregion

    #region Security (X10 motion)

    /// <summary>
    /// Publie le discovery pour un capteur Security détecté.
    /// Appelé par le SecurityHandler au premier message reçu.
    /// </summary>
    public async Task PublishSecurityDiscoveryAsync(SecurityData data, string topicName)
    {
        var id = data.SensorId.Replace("0x", "").ToLower();

        if (!ShouldPublish($"sec_{id}")) return;

        var stateTopic = $"{TopicPrefix}/sensor/security/{topicName}";
        var availabilityTopic = $"{stateTopic}/availability";
        var deviceName = $"Détecteur {data.SensorModel} ({topicName})";
        var device = BuildDeviceBlock(id, deviceName, data.SensorModel);

        // Mouvement (binary_sensor)
        await PublishDiscoveryAsync("binary_sensor", NodeId, $"{id}_motion", new Dictionary<string, object>
        {
            ["name"] = "Mouvement",
            ["unique_id"] = $"{NodeId}_{id}_motion",
            ["state_topic"] = $"{stateTopic}/motion",
            ["payload_on"] = "ON",
            ["payload_off"] = "OFF",
            ["device_class"] = "motion",
            ["device"] = device
        }, availabilityTopic);

        // Tamper
        await PublishDiscoveryAsync("binary_sensor", NodeId, $"{id}_tamper", new Dictionary<string, object>
        {
            ["name"] = "Tamper",
            ["unique_id"] = $"{NodeId}_{id}_tamper",
            ["state_topic"] = $"{stateTopic}/tamper",
            ["payload_on"] = "ON",
            ["payload_off"] = "OFF",
            ["device_class"] = "tamper",
            ["entity_category"] = "diagnostic",
            ["device"] = device
        }, availabilityTopic);

        // Batterie
        await PublishDiscoveryAsync("binary_sensor", NodeId, $"{id}_battery", new Dictionary<string, object>
        {
            ["name"] = "Batterie",
            ["unique_id"] = $"{NodeId}_{id}_battery",
            ["state_topic"] = $"{stateTopic}/battery",
            ["payload_on"] = "low",
            ["payload_off"] = "ok",
            ["device_class"] = "battery",
            ["entity_category"] = "diagnostic",
            ["device"] = device
        }, availabilityTopic);

        _logger.LogInformation("Discovery publié : détecteur {Model} [{Id}]", data.SensorModel, data.SensorId);
    }

    #endregion

    #region Somfy (cover)

    private async Task PublishSomfyDiscoveryAsync(SomfyDevice somfy)
    {
        var id = somfy.Name.ToLower().Replace(" ", "_");
        var commandTopic = $"{TopicPrefix}/command/somfy/{somfy.Name}";
        var device = BuildDeviceBlock($"somfy_{id}", $"Volet {somfy.Name}", "Somfy RTS");

        // Cover (volet roulant)
        await PublishDiscoveryAsync("cover", NodeId, $"somfy_{id}", new Dictionary<string, object>
        {
            ["name"] = somfy.Name,
            ["unique_id"] = $"{NodeId}_somfy_{id}",
            ["command_topic"] = commandTopic,
            ["payload_open"] = "{\"command\":\"up\"}",
            ["payload_close"] = "{\"command\":\"down\"}",
            ["payload_stop"] = "{\"command\":\"stop\"}",
            ["device_class"] = "shutter",
            ["icon"] = "mdi:window-shutter",
            ["device"] = device
        });

        _logger.LogInformation("Discovery publié : volet Somfy '{Name}'", somfy.Name);
    }

    #endregion

    #region Chacon (switch)

    private async Task PublishChaconDiscoveryAsync(ChaconDevice chacon)
    {
        var id = chacon.Name.ToLower().Replace(" ", "_");
        var commandTopic = $"{TopicPrefix}/command/chacon/{chacon.Name}";
        var stateTopic = $"{TopicPrefix}/sensor/chacon/{chacon.Name}/state";
        var device = BuildDeviceBlock($"chacon_{id}", $"Chacon {chacon.Name}", "Chacon/DIO");

        // Switch
        await PublishDiscoveryAsync("switch", NodeId, $"chacon_{id}", new Dictionary<string, object>
        {
            ["name"] = chacon.Name,
            ["unique_id"] = $"{NodeId}_chacon_{id}",
            ["command_topic"] = commandTopic,
            ["state_topic"] = stateTopic,
            ["payload_on"] = "{\"command\":\"on\"}",
            ["payload_off"] = "{\"command\":\"off\"}",
            ["state_on"] = "ON",
            ["state_off"] = "OFF",
            ["icon"] = "mdi:power-plug",
            ["device"] = device
        });

        _logger.LogInformation("Discovery publié : switch Chacon '{Name}'", chacon.Name);
    }

    /// <summary>
    /// Publie le discovery pour un Chacon/DIO détecté par RF (non configuré).
    /// Appelé par le Lighting2Handler au premier message reçu d'un device inconnu.
    /// </summary>
    public async Task PublishLighting2DiscoveryAsync(Lighting2Data data, string deviceName)
    {
        var id = $"{data.DeviceId}_{data.UnitCode}".Replace("0x", "").ToLower();

        if (!ShouldPublish($"chacon_{id}")) return;

        var stateTopic = $"{TopicPrefix}/sensor/chacon/{deviceName}/state";
        var availabilityTopic = $"{TopicPrefix}/sensor/chacon/{deviceName}/availability";
        var displayName = $"Chacon {data.Model} ({data.DeviceId} u{data.UnitCode})";
        var device = BuildDeviceBlock($"chacon_{id}", displayName, data.Model);

        // Switch
        await PublishDiscoveryAsync("switch", NodeId, $"chacon_{id}", new Dictionary<string, object>
        {
            ["name"] = deviceName,
            ["unique_id"] = $"{NodeId}_chacon_{id}",
            ["state_topic"] = stateTopic,
            ["state_on"] = "ON",
            ["state_off"] = "OFF",
            ["icon"] = "mdi:power-plug",
            ["device"] = device
        }, availabilityTopic);

        // Signal (diagnostic)
        await PublishDiscoveryAsync("sensor", NodeId, $"chacon_{id}_signal", new Dictionary<string, object>
        {
            ["name"] = "Signal",
            ["unique_id"] = $"{NodeId}_chacon_{id}_signal",
            ["state_topic"] = $"{TopicPrefix}/sensor/chacon/{deviceName}",
            ["value_template"] = "{{ value_json.signalLevel }}",
            ["icon"] = "mdi:signal",
            ["entity_category"] = "diagnostic",
            ["device"] = device
        }, availabilityTopic);

        _logger.LogInformation("Discovery publié : Chacon auto-détecté {Name} [{Id}]", deviceName, data.DeviceId);
    }

    #endregion

    #region Helpers

    /// <summary>Publie un message de discovery (availability liée au bridge uniquement)</summary>
    private Task PublishDiscoveryAsync(string component, string nodeId, string objectId,
        Dictionary<string, object> config)
        => PublishDiscoveryAsync(component, nodeId, objectId, config, sensorAvailabilityTopic: null);

    /// <summary>
    /// Publie un message de discovery sur le topic HA standard.
    /// Si sensorAvailabilityTopic est fourni, l'entité est disponible uniquement si
    /// le bridge ET le capteur sont online (availability_mode: all).
    /// </summary>
    private async Task PublishDiscoveryAsync(string component, string nodeId, string objectId,
        Dictionary<string, object> config, string? sensorAvailabilityTopic)
    {
        if (!string.IsNullOrEmpty(sensorAvailabilityTopic))
        {
            // Double availability : bridge + capteur individuel
            config["availability_mode"] = "all";
            config["availability"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["topic"] = $"{TopicPrefix}/availability",
                    ["value_template"] = "{{ value_json.state }}",
                    ["payload_available"] = "online",
                    ["payload_not_available"] = "offline"
                },
                new Dictionary<string, string>
                {
                    ["topic"] = sensorAvailabilityTopic,
                    ["value_template"] = "{{ value_json.state }}",
                    ["payload_available"] = "online",
                    ["payload_not_available"] = "offline"
                }
            };
        }
        else
        {
            // Availability liée au bridge uniquement
            config["availability_topic"] = $"{TopicPrefix}/availability";
            config["availability_template"] = "{{ value_json.state }}";
            config["payload_available"] = "online";
            config["payload_not_available"] = "offline";
        }

        var topic = $"{DiscoveryPrefix}/{component}/{nodeId}/{objectId}/config";
        var payload = JsonSerializer.Serialize(config, _jsonOptions);

        await _mqttService.PublishAsync(topic, payload, retain: true);
        _logger.LogDebug("Discovery → {Topic}", topic);
    }

    /// <summary>Vérifie si le device n'a pas déjà été publié (évite le spam)</summary>
    private bool ShouldPublish(string key)
    {
        lock (_lock)
        {
            return _publishedDevices.Add(key);
        }
    }

    #endregion
}
