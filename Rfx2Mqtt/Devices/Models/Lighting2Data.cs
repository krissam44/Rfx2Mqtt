namespace Rfx2Mqtt.Devices.Models;

/// <summary>
/// <b>EN:</b> Decoded data from a Lighting2 device (packet type 0x11). Covers Chacon/DIO,
/// HomeEasy EU, ANSLUT and Kambrook.<br/>
/// <b>FR:</b> Données décodées d'un équipement Lighting2 (paquet type 0x11). Couvre Chacon/DIO,
/// HomeEasy EU, ANSLUT et Kambrook.
/// </summary>
public class Lighting2Data
{
    /// <summary><b>EN:</b> Subtype string. <b>FR:</b> Sous-type.</summary>
    public string SensorType { get; set; } = "";

    /// <summary><b>EN:</b> Friendly model name. <b>FR:</b> Modèle lisible.</summary>
    public string Model { get; set; } = "";

    /// <summary><b>EN:</b> Full 4-byte hex ID (e.g. <c>"0x01A2B3C4"</c>). <b>FR:</b> ID hex complet (4 octets).</summary>
    public string DeviceId { get; set; } = "";

    /// <summary><b>EN:</b> Unit code (1–16). <b>FR:</b> Code d'unité (1–16).</summary>
    public int UnitCode { get; set; }

    /// <summary><b>EN:</b> Raw command byte. <b>FR:</b> Octet de commande brut.</summary>
    public byte CommandRaw { get; set; }

    /// <summary>
    /// <b>EN:</b> Decoded command name (on, off, set_level, group_off, group_on).<br/>
    /// <b>FR:</b> Nom de commande décodé.
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary><b>EN/FR:</b> "ON" or "OFF" state. / État "ON" ou "OFF".</summary>
    public string State { get; set; } = "OFF";

    /// <summary><b>EN:</b> Dimmer level 0–15 (raw). <b>FR:</b> Niveau dimmer 0–15 (brut).</summary>
    public int Level { get; set; }

    /// <summary><b>EN:</b> Dimmer level as percentage 0–100. <b>FR:</b> Niveau dimmer en %.</summary>
    public int LevelPercent => (int)Math.Round(Level / 15.0 * 100);

    /// <summary><b>EN:</b> Signal strength 0–15. <b>FR:</b> Force du signal 0–15.</summary>
    public int SignalLevel { get; set; }

    /// <summary><b>EN:</b> Reception timestamp (UTC). <b>FR:</b> Horodatage de réception (UTC).</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <b>EN:</b> Local-time string for display.<br/>
    /// <b>FR:</b> Chaîne en heure locale pour affichage.
    /// </summary>
    public string ReceivedAtFormatted => ReceivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
}

/// <summary>
/// <b>EN:</b> Known subtypes for Lighting2 (packet type 0x11).<br/>
/// <b>FR:</b> Sous-types connus pour Lighting2 (paquet type 0x11).
/// </summary>
public static class Lighting2SubTypes
{
    /// <summary><b>EN/FR:</b> AC / Chacon / DIO.</summary>
    public const byte AC = 0x00;
    /// <summary><b>EN/FR:</b> HomeEasy EU.</summary>
    public const byte HomeEasyEU = 0x01;
    /// <summary><b>EN/FR:</b> ANSLUT.</summary>
    public const byte ANSLUT = 0x02;
    /// <summary><b>EN/FR:</b> Kambrook.</summary>
    public const byte Kambrook = 0x03;

    /// <summary>
    /// <b>EN:</b> Resolves a subtype byte to a model name.<br/>
    /// <b>FR:</b> Renvoie le nom de modèle pour un octet de sous-type.
    /// </summary>
    public static string GetModelName(byte subType) => subType switch
    {
        AC => "Chacon/DIO/AC",
        HomeEasyEU => "HomeEasy EU",
        ANSLUT => "ANSLUT",
        Kambrook => "Kambrook",
        _ => $"Inconnu (0x{subType:X2})"
    };
}

/// <summary>
/// <b>EN:</b> Lighting2 command bytes.<br/>
/// <b>FR:</b> Octets de commande Lighting2.
/// </summary>
public static class Lighting2Commands
{
    /// <summary><b>EN/FR:</b> Turn off.</summary>
    public const byte Off = 0x00;
    /// <summary><b>EN/FR:</b> Turn on.</summary>
    public const byte On = 0x01;
    /// <summary><b>EN/FR:</b> Set dimmer level.</summary>
    public const byte SetLevel = 0x02;
    /// <summary><b>EN/FR:</b> Group off (broadcast).</summary>
    public const byte GroupOff = 0x03;
    /// <summary><b>EN/FR:</b> Group on (broadcast).</summary>
    public const byte GroupOn = 0x04;
    /// <summary><b>EN/FR:</b> Set group dimmer level (broadcast).</summary>
    public const byte SetGroupLevel = 0x05;

    /// <summary>
    /// <b>EN:</b> Resolves a command byte to its string name.<br/>
    /// <b>FR:</b> Renvoie le nom textuel d'un octet de commande.
    /// </summary>
    public static string Decode(byte cmd) => cmd switch
    {
        Off => "off",
        On => "on",
        SetLevel => "set_level",
        GroupOff => "group_off",
        GroupOn => "group_on",
        SetGroupLevel => "set_group_level",
        _ => $"unknown_0x{cmd:X2}"
    };
}
