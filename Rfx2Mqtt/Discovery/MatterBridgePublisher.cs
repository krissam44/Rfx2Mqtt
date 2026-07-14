using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Mqtt;

namespace Rfx2Mqtt.Discovery;

/// <summary>
/// <b>EN:</b> Publishes <c>rfxcom/devices</c> (retained JSON array) so Matter bridges such as
/// MatterBridge can enumerate and register all Rfx2Mqtt devices dynamically without hardcoding
/// device types. Re-published on every <see cref="IDeviceRepository.Changed"/> event.
/// <br/><br/>
/// <b>FR:</b> Publie <c>rfxcom/devices</c> (tableau JSON retained) pour que les bridges Matter
/// (ex. MatterBridge) puissent énumérer et enregistrer dynamiquement tous les appareils
/// Rfx2Mqtt sans hardcoder les types. Republié à chaque événement <see cref="IDeviceRepository.Changed"/>.
/// </summary>
public class MatterBridgePublisher
{
    private readonly MqttService _mqttService;
    private readonly MqttTopics _topics;
    private readonly IDeviceRepository _devices;
    private readonly ILogger<MatterBridgePublisher> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public MatterBridgePublisher(
        MqttService mqttService,
        MqttTopics topics,
        IDeviceRepository devices,
        ILogger<MatterBridgePublisher> logger)
    {
        _mqttService = mqttService;
        _topics = topics;
        _devices = devices;
        _logger = logger;

        // Re-publish whenever the user saves the device list from the UI.
        // Changed is a sync event, so kick async work on the thread pool.
        _devices.Changed += OnDevicesChanged;
    }

    private void OnDevicesChanged()
    {
        _ = Task.Run(async () =>
        {
            try { await PublishAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Erreur republication {Topic}", _topics.Devices); }
        });
    }

    /// <summary>
    /// <b>EN:</b> Builds and publishes the device inventory to <c>rfxcom/devices</c>.<br/>
    /// <b>FR:</b> Construit et publie l'inventaire des appareils sur <c>rfxcom/devices</c>.
    /// </summary>
    public async Task PublishAsync()
    {
        var descriptors = BuildDescriptors(_devices.Snapshot);
        var payload = JsonSerializer.Serialize(descriptors, _jsonOptions);
        await _mqttService.PublishAsync(_topics.Devices, payload, retain: true);
        _logger.LogInformation("Publié {Topic} : {Count} appareil(s)", _topics.Devices, descriptors.Count);
    }

    private List<DeviceDescriptor> BuildDescriptors(DeviceCollection devices)
    {
        var p = _topics.Prefix;
        var list = new List<DeviceDescriptor>();

        foreach (var d in devices.Oregon)
            list.Add(new DeviceDescriptor(
                UniqueId:   $"th_{Slug(d.Name)}",
                Name:       d.Name,
                Type:       MqttTopics.KindTh,
                MatterType: "TemperatureSensor",
                Topics: new()
                {
                    ["state"]        = $"{p}/sensor/th/{d.Name}",
                    ["temperature"]  = $"{p}/sensor/th/{d.Name}/temperature",
                    ["humidity"]     = $"{p}/sensor/th/{d.Name}/humidity",
                    ["battery"]      = $"{p}/sensor/th/{d.Name}/battery",
                    ["availability"] = $"{p}/sensor/th/{d.Name}/availability",
                }));

        foreach (var d in devices.Security)
            list.Add(new DeviceDescriptor(
                UniqueId:   $"security_{Slug(d.Name)}",
                Name:       d.Name,
                Type:       MqttTopics.KindSecurity,
                MatterType: "OccupancySensor",
                Topics: new()
                {
                    ["state"]        = $"{p}/sensor/security/{d.Name}",
                    ["motion"]       = $"{p}/sensor/security/{d.Name}/motion",
                    ["occupancy"]    = $"{p}/sensor/security/{d.Name}/occupancy",
                    ["availability"] = $"{p}/sensor/security/{d.Name}/availability",
                }));

        foreach (var d in devices.Somfy)
            list.Add(new DeviceDescriptor(
                UniqueId:   $"somfy_{Slug(d.Name)}",
                Name:       d.Name,
                Type:       MqttTopics.KindSomfy,
                MatterType: "WindowCovering",
                Topics: new()
                {
                    ["event"]   = $"{p}/event/somfy/{d.Name}",
                    ["command"] = $"{p}/command/somfy/{d.Name}",
                }));

        foreach (var d in devices.Chacon)
            list.Add(new DeviceDescriptor(
                UniqueId:   $"chacon_{Slug(d.Name)}",
                Name:       d.Name,
                Type:       MqttTopics.KindChacon,
                MatterType: "OnOffPlugin",
                Topics: new()
                {
                    ["state"]   = $"{p}/sensor/chacon/{d.Name}/state",
                    ["full"]    = $"{p}/sensor/chacon/{d.Name}",
                    ["command"] = $"{p}/command/chacon/{d.Name}",
                }));

        return list;
    }

    private static string Slug(string name) =>
        name.ToLowerInvariant().Replace(" ", "_");

    private sealed record DeviceDescriptor(
        string UniqueId,
        string Name,
        string Type,
        string MatterType,
        Dictionary<string, string> Topics);
}
