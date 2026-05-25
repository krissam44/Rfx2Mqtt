namespace Rfx2Mqtt.Devices.Models;

/// <summary>
/// <b>EN:</b> Decoded data from a temperature / humidity probe (Oregon, Bresser, etc.) coming
/// from packet types 0x52 (TempHumidity) and 0x54 (TempHumBaro).<br/>
/// <b>FR:</b> Données décodées d'une sonde T°/Humidité (Oregon, Bresser, etc.) issues des paquets
/// de type 0x52 (TempHumidity) et 0x54 (TempHumBaro).
/// </summary>
public class TempHumidityData
{
    /// <summary><b>EN:</b> Sensor subtype string (brand/model). <b>FR:</b> Sous-type du capteur (marque/modèle).</summary>
    public string SensorType { get; set; } = "";

    /// <summary><b>EN:</b> Sensor unique ID (e.g. <c>"0xA201"</c>). <b>FR:</b> ID unique du capteur.</summary>
    public string SensorId { get; set; } = "";

    /// <summary><b>EN:</b> Temperature in °C. <b>FR:</b> Température en °C.</summary>
    public double Temperature { get; set; }

    /// <summary><b>EN:</b> Humidity in %. <b>FR:</b> Humidité en %.</summary>
    public int Humidity { get; set; }

    /// <summary>
    /// <b>EN:</b> Humidity comfort status: 0=Normal, 1=Comfort, 2=Dry, 3=Wet.<br/>
    /// <b>FR:</b> Statut de confort humidité : 0=Normal, 1=Comfort, 2=Dry, 3=Wet.
    /// </summary>
    public int HumidityStatus { get; set; }

    /// <summary>
    /// <b>EN:</b> Barometric pressure in hPa (only for packet type 0x54).<br/>
    /// <b>FR:</b> Pression barométrique en hPa (uniquement type 0x54).
    /// </summary>
    public double? Barometer { get; set; }

    /// <summary>
    /// <b>EN:</b> Battery level 0–9 (0 = low/empty, 9 = full).<br/>
    /// <b>FR:</b> Niveau de batterie 0–9 (0 = faible/vide, 9 = pleine).
    /// </summary>
    public int BatteryLevel { get; set; }

    /// <summary><b>EN:</b> Signal strength 0–15. <b>FR:</b> Force du signal 0–15.</summary>
    public int SignalLevel { get; set; }

    /// <summary><b>EN:</b> Sensor channel (1–3, some models). <b>FR:</b> Canal du capteur (1–3, selon modèle).</summary>
    public int Channel { get; set; }

    /// <summary>
    /// <b>EN:</b> Reception timestamp (UTC).<br/>
    /// <b>FR:</b> Horodatage de réception (UTC).
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <b>EN:</b> Local-time string for display (e.g. <c>"02/04/2026 14:35:12"</c>).<br/>
    /// <b>FR:</b> Chaîne en heure locale pour affichage.
    /// </summary>
    public string ReceivedAtFormatted => ReceivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    /// <summary>
    /// <b>EN:</b> Friendly sensor model string (e.g. <c>"Oregon THGN132"</c>).<br/>
    /// <b>FR:</b> Description lisible du modèle.
    /// </summary>
    public string SensorModel { get; set; } = "";
}

/// <summary>
/// <b>EN:</b> Known subtypes for packet 0x52 (Temp + Humidity).<br/>
/// <b>FR:</b> Sous-types connus pour le paquet 0x52 (Temp + Humidity).
/// </summary>
public static class TempHumiditySubTypes
{
    /// <summary><b>EN/FR:</b> Oregon THGN122/132, THGR122/228/238/268.</summary>
    public const byte THGN122_THGN132 = 0x01;
    /// <summary><b>EN/FR:</b> Oregon THGR810, THGN800.</summary>
    public const byte THGR810_THGN800 = 0x02;
    /// <summary><b>EN/FR:</b> Oregon RTGR328.</summary>
    public const byte RTGR328 = 0x03;
    /// <summary><b>EN/FR:</b> Oregon THGR328.</summary>
    public const byte THGR328 = 0x04;
    /// <summary><b>EN/FR:</b> Oregon WTGR800.</summary>
    public const byte WTGR800 = 0x05;
    /// <summary><b>EN/FR:</b> Oregon THGR918, THGRN228, THGN500.</summary>
    public const byte THGR918 = 0x06;
    /// <summary><b>EN/FR:</b> TFA TS34C / Cresta.</summary>
    public const byte TFA_TS34C = 0x07;
    /// <summary><b>EN/FR:</b> WT260, WT450H, WT440H, WT450.</summary>
    public const byte WT260_WT450 = 0x08;
    /// <summary><b>EN/FR:</b> Viking 02035 / 02038 / TSS320.</summary>
    public const byte VIKING_02035 = 0x09;
    /// <summary><b>EN/FR:</b> Rubicson.</summary>
    public const byte RUBICSON = 0x0A;
    /// <summary><b>EN/FR:</b> EW109.</summary>
    public const byte EW109 = 0x0B;
    /// <summary><b>EN/FR:</b> Imagintronix Soil sensor.</summary>
    public const byte IMAGINTRONIX = 0x0C;
    /// <summary><b>EN/FR:</b> Alecto WS1700.</summary>
    public const byte ALECTO_WS1700 = 0x0D;
    /// <summary><b>EN/FR:</b> Alecto WH2, Auriol H13726, Bresser Temeo.</summary>
    public const byte ALECTO_WH2 = 0x0E;

    /// <summary>
    /// <b>EN:</b> Resolves a subtype byte to a human-readable model name.<br/>
    /// <b>FR:</b> Renvoie le nom de modèle lisible pour un octet de sous-type.
    /// </summary>
    public static string GetModelName(byte subType) => subType switch
    {
        THGN122_THGN132 => "Oregon THGN122/132",
        THGR810_THGN800 => "Oregon THGR810/THGN800",
        RTGR328 => "Oregon RTGR328",
        THGR328 => "Oregon THGR328",
        WTGR800 => "Oregon WTGR800",
        THGR918 => "Oregon THGR918/THGN500",
        TFA_TS34C => "TFA TS34C / Cresta",
        WT260_WT450 => "WT260/WT450H",
        VIKING_02035 => "Viking 02035/02038",
        RUBICSON => "Rubicson",
        EW109 => "EW109",
        IMAGINTRONIX => "Imagintronix",
        ALECTO_WS1700 => "Alecto WS1700",
        ALECTO_WH2 => "Bresser Temeo / Alecto WH2",
        _ => $"Inconnu (0x{subType:X2})"
    };
}

/// <summary>
/// <b>EN:</b> Known subtypes for packet 0x54 (Temp + Humidity + Barometer).<br/>
/// <b>FR:</b> Sous-types connus pour le paquet 0x54 (Temp + Humidity + Baro).
/// </summary>
public static class TempHumBaroSubTypes
{
    /// <summary><b>EN/FR:</b> Oregon BTHR918.</summary>
    public const byte BTHR918 = 0x01;
    /// <summary><b>EN/FR:</b> Oregon BTHR918N, BTHR968.</summary>
    public const byte BTHR918N_BTHR968 = 0x02;

    /// <summary>
    /// <b>EN:</b> Resolves a subtype byte to a human-readable model name.<br/>
    /// <b>FR:</b> Renvoie le nom de modèle lisible pour un octet de sous-type.
    /// </summary>
    public static string GetModelName(byte subType) => subType switch
    {
        BTHR918 => "Oregon BTHR918",
        BTHR918N_BTHR968 => "Oregon BTHR918N/968",
        _ => $"Inconnu (0x{subType:X2})"
    };
}
