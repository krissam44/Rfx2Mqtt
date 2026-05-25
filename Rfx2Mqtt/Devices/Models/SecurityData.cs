using System.Text.Json.Serialization;

namespace Rfx2Mqtt.Devices.Models;

/// <summary>
/// <b>EN:</b> Decoded data from a Security1 sensor (packet type 0x20). Includes X10 motion
/// detectors, door/window contacts, smoke detectors, etc. Some properties are normalized to
/// Zigbee2MQTT field names so downstream consumers like DomoMud can use them without remapping.<br/>
/// <b>FR:</b> Données décodées d'un capteur Security1 (paquet type 0x20). Inclut les détecteurs
/// de mouvement X10, contacts porte/fenêtre, détecteurs de fumée, etc. Certaines propriétés sont
/// normalisées aux noms de champs Zigbee2MQTT pour que les consommateurs en aval (DomoMud) les
/// utilisent sans remapping.
/// </summary>
public class SecurityData
{
    /// <summary><b>EN:</b> Subtype string. <b>FR:</b> Sous-type.</summary>
    public string SensorType { get; set; } = "";

    /// <summary><b>EN:</b> Friendly model name. <b>FR:</b> Modèle lisible.</summary>
    public string SensorModel { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> Unique 3-byte sensor ID (e.g. <c>"0x00A1B2"</c>).<br/>
    /// <b>FR:</b> ID unique sur 3 octets (ex : <c>"0x00A1B2"</c>).
    /// </summary>
    public string SensorId { get; set; } = "";

    /// <summary><b>EN:</b> Raw status byte. <b>FR:</b> Octet de statut brut.</summary>
    public byte StatusRaw { get; set; }

    /// <summary>
    /// <b>EN:</b> Decoded status (e.g. <c>"motion"</c>, <c>"no_motion"</c>, <c>"alarm"</c>, <c>"tamper"</c>).<br/>
    /// <b>FR:</b> Statut décodé.
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary><b>EN:</b> True if motion is currently active. <b>FR:</b> Vrai si mouvement actif.</summary>
    public bool Motion { get; set; }

    /// <summary>
    /// <b>EN:</b> Tamper flag — sensor opened or yanked off the wall.<br/>
    /// <b>FR:</b> Flag tamper — capteur ouvert ou arraché.
    /// </summary>
    public bool Tamper { get; set; }

    /// <summary>
    /// <b>EN:</b> Battery level 0–9 (0 = low/empty, 9 = full).<br/>
    /// <b>FR:</b> Niveau de batterie 0–9 (0 = faible/vide, 9 = pleine).
    /// </summary>
    public int BatteryLevel { get; set; }

    /// <summary><b>EN:</b> Signal strength 0–15. <b>FR:</b> Force du signal 0–15.</summary>
    public int SignalLevel { get; set; }

    /// <summary><b>EN:</b> Reception timestamp (UTC). <b>FR:</b> Horodatage de réception (UTC).</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <b>EN:</b> Local-time string for display.<br/>
    /// <b>FR:</b> Chaîne en heure locale pour affichage.
    /// </summary>
    public string ReceivedAtFormatted => ReceivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    // ── Normalized fields (Zigbee2MQTT compatibility) ────────────────────────────

    /// <summary>
    /// <b>EN:</b> Battery percentage 0–100 (normalized from <see cref="BatteryLevel"/> 0–9).<br/>
    /// <b>FR:</b> Pourcentage batterie 0–100 (normalisé depuis <see cref="BatteryLevel"/> 0–9).
    /// </summary>
    public int Battery => BatteryLevel * 100 / 9;

    /// <summary>
    /// <b>EN:</b> Link quality 0–255 (normalized from <see cref="SignalLevel"/> 0–15).
    /// Serialized as <c>"linkquality"</c> to match Zigbee2MQTT.<br/>
    /// <b>FR:</b> Qualité de liaison 0–255 (normalisée depuis <see cref="SignalLevel"/> 0–15).
    /// Sérialisée en <c>"linkquality"</c> pour matcher Zigbee2MQTT.
    /// </summary>
    [JsonPropertyName("linkquality")]
    public int LinkQuality => SignalLevel * 255 / 15;

    /// <summary>
    /// <b>EN:</b> Alias of <see cref="Motion"/> — matches Zigbee2MQTT <c>occupancy</c>.<br/>
    /// <b>FR:</b> Alias de <see cref="Motion"/> — compatibilité Zigbee2MQTT <c>occupancy</c>.
    /// </summary>
    public bool Occupancy => Motion;
}

/// <summary>
/// <b>EN:</b> Known subtypes for Security1 (packet type 0x20).<br/>
/// <b>FR:</b> Sous-types connus pour Security1 (paquet type 0x20).
/// </summary>
public static class Security1SubTypes
{
    /// <summary><b>EN/FR:</b> X10 Security.</summary>
    public const byte X10Security = 0x00;
    /// <summary><b>EN/FR:</b> X10 Security Motion detector.</summary>
    public const byte X10SecurityMotion = 0x01;
    /// <summary><b>EN/FR:</b> X10 Security Remote control.</summary>
    public const byte X10SecurityRemote = 0x02;
    /// <summary><b>EN/FR:</b> KD101 smoke detector.</summary>
    public const byte KD101 = 0x03;
    /// <summary><b>EN/FR:</b> Visonic PowerCode door/window contact.</summary>
    public const byte RM174RF = 0x04;
    /// <summary><b>EN/FR:</b> Atlantic sector alarm.</summary>
    public const byte SecXX36 = 0x05;
    /// <summary><b>EN/FR:</b> SA30 smoke detector.</summary>
    public const byte SA30 = 0x06;
    /// <summary><b>EN/FR:</b> Meiantech security.</summary>
    public const byte Meiantech = 0x07;

    /// <summary>
    /// <b>EN:</b> Resolves a subtype byte to a model name.<br/>
    /// <b>FR:</b> Renvoie le nom de modèle pour un octet de sous-type.
    /// </summary>
    public static string GetModelName(byte subType) => subType switch
    {
        X10Security => "X10 Security",
        X10SecurityMotion => "X10 Security Motion",
        X10SecurityRemote => "X10 Security Remote",
        KD101 => "KD101 Smoke Detector",
        RM174RF => "Visonic PowerCode",
        SecXX36 => "Atlantic Sector",
        SA30 => "SA30 Smoke Detector",
        Meiantech => "Meiantech",
        _ => $"Inconnu (0x{subType:X2})"
    };
}

/// <summary>
/// <b>EN:</b> Security1 status byte values. Bit 7 carries a tamper flag, bits 0–6 carry the
/// actual status code.<br/>
/// <b>FR:</b> Valeurs de l'octet de statut Security1. Le bit 7 porte le flag tamper, les bits
/// 0–6 portent le code de statut réel.
/// </summary>
public static class SecurityStatus
{
    /// <summary><b>EN/FR:</b> Normal (no alarm).</summary>
    public const byte Normal = 0x00;
    /// <summary><b>EN/FR:</b> Normal with delay.</summary>
    public const byte NormalDelayed = 0x01;
    /// <summary><b>EN/FR:</b> Alarm triggered.</summary>
    public const byte Alarm = 0x02;
    /// <summary><b>EN/FR:</b> Alarm with delay.</summary>
    public const byte AlarmDelayed = 0x03;
    /// <summary><b>EN/FR:</b> Motion detected.</summary>
    public const byte Motion = 0x04;
    /// <summary><b>EN/FR:</b> No motion (explicit clear).</summary>
    public const byte NoMotion = 0x05;
    /// <summary><b>EN/FR:</b> Panic.</summary>
    public const byte Panic = 0x06;
    /// <summary><b>EN/FR:</b> End of panic.</summary>
    public const byte EndPanic = 0x07;
    /// <summary><b>EN/FR:</b> Dark detected.</summary>
    public const byte DarkDetected = 0x14;
    /// <summary><b>EN/FR:</b> Light detected.</summary>
    public const byte LightDetected = 0x15;
    /// <summary><b>EN/FR:</b> Battery low.</summary>
    public const byte BatteryLow = 0x16;
    /// <summary>
    /// <b>EN:</b> Bit 7 — OR'd with the status when sensor is tampered.<br/>
    /// <b>FR:</b> Bit 7 — combiné en OR avec le statut quand le capteur est ouvert/arraché.
    /// </summary>
    public const byte TamperFlag = 0x80;

    /// <summary>
    /// <b>EN:</b> Decodes the status byte into a human-friendly string (appending
    /// <c>"+tamper"</c> when the tamper bit is set).<br/>
    /// <b>FR:</b> Décode l'octet de statut en chaîne lisible (en ajoutant <c>"+tamper"</c>
    /// si le bit tamper est positionné).
    /// </summary>
    public static string Decode(byte status)
    {
        var baseStatus = (byte)(status & 0x7F);
        var name = baseStatus switch
        {
            Normal => "normal",
            NormalDelayed => "normal_delayed",
            Alarm => "alarm",
            AlarmDelayed => "alarm_delayed",
            Motion => "motion",
            NoMotion => "no_motion",
            Panic => "panic",
            EndPanic => "end_panic",
            DarkDetected => "dark",
            LightDetected => "light",
            BatteryLow => "battery_low",
            _ => $"unknown_0x{baseStatus:X2}"
        };

        if ((status & TamperFlag) != 0)
            name += "+tamper";

        return name;
    }
}
