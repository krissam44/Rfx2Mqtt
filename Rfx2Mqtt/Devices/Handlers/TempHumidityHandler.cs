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
/// <b>EN:</b> Handler for temperature/humidity probes (packet 0x52) and T°/Humidity/Barometer
/// probes (packet 0x54). Supported brands: Oregon Scientific, Bresser, Viking, Alecto, TFA,
/// Rubicson, etc. Includes an anti-noise plausibility filter that rejects neighbor and
/// RF-parasite frames.<br/>
/// <b>FR:</b> Handler pour les sondes T°/Humidité (paquet 0x52) et T°/Humidité/Baromètre
/// (paquet 0x54). Marques supportées : Oregon Scientific, Bresser, Viking, Alecto, TFA,
/// Rubicson, etc. Inclut un filtre de plausibilité anti-parasites pour rejeter les trames
/// des voisins et les bruits RF.
///
/// <para><b>EN/FR:</b> Frame layout 0x52 (after header) / Format trame 0x52 (après header) :</para>
/// <list type="bullet">
///   <item>Data[0..1] = sensor ID (2 bytes / 2 octets)</item>
///   <item>Data[2] = temp high (bit7 = sign, bits 0-6 = MSB)</item>
///   <item>Data[3] = temp low (LSB)</item>
///   <item>Data[4] = humidity 0-100% / humidité</item>
///   <item>Data[5] = humidity comfort status (0=Normal, 1=Comfort, 2=Dry, 3=Wet)</item>
///   <item>Data[6] = battery (high nibble) + signal (low nibble)</item>
/// </list>
///
/// <para><b>EN/FR:</b> Frame layout 0x54 / Format trame 0x54 :</para>
/// <list type="bullet">
///   <item>Data[0..5] = same as 0x52 / identique à 0x52</item>
///   <item>Data[6..7] = barometer pressure / pression barométrique</item>
///   <item>Data[8] = forecast (1=Sunny, 2=Partly Cloudy, 3=Cloudy, 4=Rain)</item>
///   <item>Data[9] = battery + signal</item>
/// </list>
/// </summary>
public class TempHumidityHandler : IPacketHandler
{
    private readonly ILogger<TempHumidityHandler> _logger;
    private readonly MqttTopics _topics;
    private readonly IDeviceRepository _devices;
    private readonly PermitJoinState _permitJoin;
    private readonly HomeAssistantDiscoveryService _discovery;
    private readonly AvailabilityService _availability;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TempHumidityHandler(
        ILogger<TempHumidityHandler> logger,
        MqttTopics topics,
        IDeviceRepository devices,
        PermitJoinState permitJoin,
        HomeAssistantDiscoveryService discovery,
        AvailabilityService availability)
    {
        _logger = logger;
        _topics = topics;
        _devices = devices;
        _permitJoin = permitJoin;
        _discovery = discovery;
        _availability = availability;
    }

    public IReadOnlyList<byte> SupportedPacketTypes { get; } =
        [PacketTypes.TempHumidity, PacketTypes.TempHumBaro];

    public bool CanHandle(RfxComPacket packet)
        => packet.PacketType == PacketTypes.TempHumidity
        || packet.PacketType == PacketTypes.TempHumBaro;

    public async Task<IEnumerable<MqttMessage>> HandleAsync(RfxComPacket packet)
    {
        var messages = new List<MqttMessage>();

        try
        {
            var data = packet.PacketType switch
            {
                PacketTypes.TempHumidity => ParseTempHumidity(packet),
                PacketTypes.TempHumBaro => ParseTempHumBaro(packet),
                _ => null
            };

            if (data is null) return messages;

            // Filtre anti-parasites : rejeter les valeurs aberrantes (bruits RF, voisins)
            if (!IsPlausible(data))
            {
                _logger.LogWarning(
                    "Parasite rejeté [{Id}] : {Temp}°C {Hum}% (signal:{Sig}) → valeurs hors limites",
                    data.SensorId, data.Temperature, data.Humidity, data.SignalLevel);
                return messages;
            }

            // Chercher le device dans la config (nom friendly)
            var configured = _devices.Snapshot.Oregon
                .FirstOrDefault(d => d.Id.Equals(data.SensorId, StringComparison.OrdinalIgnoreCase));

            // PermitJoin : si false et device non configuré → ignorer
            if (!_permitJoin.IsEnabled && configured is null)
            {
                _logger.LogDebug("Sonde [{Id}] ignorée (PermitJoin=false, non configurée)", data.SensorId);
                return messages;
            }

            var topicName = configured?.Name ?? data.SensorId;
            var kind = data.Barometer.HasValue ? MqttTopics.KindThBaro : MqttTopics.KindTh;
            var topicBase = _topics.SensorBase(kind, topicName);

            // Auto-discovery Home Assistant (une seule fois par sonde)
            await _discovery.PublishTempHumidityDiscoveryAsync(data, topicName);

            // Signaler la sonde comme active (availability)
            await _availability.ReportSeenAsync(
                $"{kind}/{topicName}",
                _topics.SensorAvailability(kind, topicName));

            // Message principal avec toutes les données en JSON
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            messages.Add(new MqttMessage(topicBase, json, Retain: true));

            // Messages individuels pour faciliter l'intégration HA / autres
            messages.Add(new MqttMessage(
                _topics.SensorAttribute(kind, topicName, MqttTopics.AttrTemperature),
                data.Temperature.ToString("F1", CultureInfo.InvariantCulture), Retain: true));
            messages.Add(new MqttMessage(
                _topics.SensorAttribute(kind, topicName, MqttTopics.AttrHumidity),
                data.Humidity.ToString(CultureInfo.InvariantCulture), Retain: true));
            messages.Add(new MqttMessage(
                _topics.SensorAttribute(kind, topicName, MqttTopics.AttrBattery),
                BatteryLabel(data.BatteryLevel), Retain: true));
            messages.Add(new MqttMessage(
                _topics.SensorAttribute(kind, topicName, MqttTopics.AttrSignal),
                data.SignalLevel.ToString(CultureInfo.InvariantCulture), Retain: true));

            if (data.Barometer.HasValue)
            {
                messages.Add(new MqttMessage(
                    _topics.SensorAttribute(kind, topicName, MqttTopics.AttrBarometer),
                    data.Barometer.Value.ToString("F0", CultureInfo.InvariantCulture), Retain: true));
            }

            _logger.LogDebug(
                "Sonde {Model} [{Id}]{Name} ch{Channel} : {Temp}°C {Hum}%{Baro} (signal:{Sig} bat:{Bat})",
                data.SensorModel, data.SensorId,
                configured is not null ? $" \"{configured.Name}\"" : "",
                data.Channel,
                data.Temperature, data.Humidity,
                data.Barometer.HasValue ? $" {data.Barometer}hPa" : "",
                data.SignalLevel,
                BatteryLabel(data.BatteryLevel).ToUpperInvariant());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur parsing paquet 0x{Type:X2} : {Hex}",
                packet.PacketType, RfxComProtocol.FormatHex(packet.RawBytes));
        }

        return messages;
    }

    /// <summary>
    /// Seuil batterie RFXCom : niveau 0–3 = low (à remplacer bientôt), 4–9 = ok.
    /// Le code précédent ne signalait que niveau 0 (batterie morte) — trop tard
    /// pour anticiper un changement de pile.
    /// </summary>
    internal static string BatteryLabel(int level) => level <= 3 ? "low" : "ok";

    #region Parsing 0x52 - Temp/Humidity

    private TempHumidityData? ParseTempHumidity(RfxComPacket packet)
    {
        if (packet.Data.Length < 7)
        {
            _logger.LogWarning("Paquet 0x52 trop court ({Len} octets data, 7 attendus)", packet.Data.Length);
            return null;
        }

        var d = packet.Data;
        var sensorId = $"0x{d[0]:X2}{d[1]:X2}";
        var (temperature, _) = DecodeTemperature(d[2], d[3]);
        var batterySignal = DecodeBatterySignal(d[6]);

        return new TempHumidityData
        {
            SensorType = $"0x52/{packet.SubType:X2}",
            SensorModel = TempHumiditySubTypes.GetModelName(packet.SubType),
            SensorId = sensorId,
            Channel = ExtractChannel(packet.SubType, d[0], d[1]),
            Temperature = temperature,
            Humidity = d[4],
            HumidityStatus = d[5],
            BatteryLevel = batterySignal.Battery,
            SignalLevel = batterySignal.Signal,
            ReceivedAt = packet.ReceivedAt
        };
    }

    #endregion

    #region Parsing 0x54 - Temp/Humidity/Barometer

    private TempHumidityData? ParseTempHumBaro(RfxComPacket packet)
    {
        if (packet.Data.Length < 10)
        {
            _logger.LogWarning("Paquet 0x54 trop court ({Len} octets data, 10 attendus)", packet.Data.Length);
            return null;
        }

        var d = packet.Data;
        var sensorId = $"0x{d[0]:X2}{d[1]:X2}";
        var (temperature, _) = DecodeTemperature(d[2], d[3]);
        var barometer = (d[6] << 8) + d[7];
        var batterySignal = DecodeBatterySignal(d[9]);

        return new TempHumidityData
        {
            SensorType = $"0x54/{packet.SubType:X2}",
            SensorModel = TempHumBaroSubTypes.GetModelName(packet.SubType),
            SensorId = sensorId,
            Channel = ExtractChannel(packet.SubType, d[0], d[1]),
            Temperature = temperature,
            Humidity = d[4],
            HumidityStatus = d[5],
            Barometer = barometer,
            BatteryLevel = batterySignal.Battery,
            SignalLevel = batterySignal.Signal,
            ReceivedAt = packet.ReceivedAt
        };
    }

    #endregion

    #region Helpers décodage

    internal static (double Temperature, bool IsNegative) DecodeTemperature(byte high, byte low)
    {
        bool negative = (high & 0x80) != 0;
        double temp = ((high & 0x7F) * 256 + low) / 10.0;
        if (negative) temp = -temp;
        return (temp, negative);
    }

    internal static (int Battery, int Signal) DecodeBatterySignal(byte value)
    {
        int battery = (value >> 4) & 0x0F;
        int signal = value & 0x0F;
        return (battery, signal);
    }

    internal static int ExtractChannel(byte subType, byte id1, byte id2)
    {
        return subType switch
        {
            TempHumiditySubTypes.THGN122_THGN132
                or TempHumiditySubTypes.THGR810_THGN800
                or TempHumiditySubTypes.THGR918 => (id2 >> 4) & 0x0F,
            TempHumiditySubTypes.ALECTO_WH2 => 0,
            _ => (id1 >> 4) & 0x0F
        };
    }

    #endregion

    #region Filtre anti-parasites

    private const double MinTemperature = -40.0;
    private const double MaxTemperature = 60.0;
    private const int MaxHumidity = 100;
    private const double MinBarometer = 800.0;
    private const double MaxBarometer = 1100.0;

    internal static bool IsPlausible(TempHumidityData data)
    {
        if (data.Temperature < MinTemperature || data.Temperature > MaxTemperature)
            return false;
        if (data.Humidity < 0 || data.Humidity > MaxHumidity)
            return false;
        if (data.Barometer.HasValue &&
            (data.Barometer.Value < MinBarometer || data.Barometer.Value > MaxBarometer))
            return false;
        return true;
    }

    #endregion
}
