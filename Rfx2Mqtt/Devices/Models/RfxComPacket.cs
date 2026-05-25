namespace Rfx2Mqtt.Devices.Models;

/// <summary>
/// <b>EN:</b> Decoded representation of a raw RFXCom binary frame.
/// Frame layout: <c>[length] [type] [subtype] [seqnbr] [data...]</c><br/>
/// <b>FR:</b> Représentation décodée d'une trame binaire brute du RFXCom.
/// Format trame : <c>[length] [type] [subtype] [seqnbr] [data...]</c>
/// </summary>
public class RfxComPacket
{
    /// <summary>
    /// <b>EN:</b> Frame length byte (not including the length byte itself).<br/>
    /// <b>FR:</b> Octet de longueur de la trame (sans compter cet octet lui-même).
    /// </summary>
    public byte Length { get; set; }

    /// <summary>
    /// <b>EN:</b> Packet type (e.g. 0x01 = Status, 0x52 = TempHumidity, 0x1A = RFY).<br/>
    /// <b>FR:</b> Type de paquet (ex : 0x01 = Status, 0x52 = TempHumidity, 0x1A = RFY).
    /// </summary>
    public byte PacketType { get; set; }

    /// <summary>
    /// <b>EN:</b> Subtype byte (discriminates sensor model within a packet type).<br/>
    /// <b>FR:</b> Octet de sous-type (discrimine le modèle de capteur dans un type de paquet).
    /// </summary>
    public byte SubType { get; set; }

    /// <summary>
    /// <b>EN:</b> Cyclic sequence number (0–255).<br/>
    /// <b>FR:</b> Numéro de séquence cyclique (0–255).
    /// </summary>
    public byte SequenceNumber { get; set; }

    /// <summary>
    /// <b>EN:</b> Payload bytes after the seqnbr.<br/>
    /// <b>FR:</b> Octets de données après le seqnbr.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> Full raw frame including the length byte.<br/>
    /// <b>FR:</b> Trame brute complète, octet de longueur inclus.
    /// </summary>
    public byte[] RawBytes { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> UTC reception timestamp — convert to local time only for display.<br/>
    /// <b>FR:</b> Horodatage de réception UTC — convertir en heure locale uniquement pour l'affichage.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <b>EN:</b> Hex-friendly debug representation.<br/>
    /// <b>FR:</b> Représentation lisible (hex) pour le debug.
    /// </summary>
    public override string ToString()
        => $"Packet Type=0x{PacketType:X2} SubType=0x{SubType:X2} Seq={SequenceNumber} Len={Length} Data=[{string.Join(" ", Data.Select(b => $"0x{b:X2}"))}]";
}

/// <summary>
/// <b>EN:</b> Known RFXCom packet type constants (see RFXCom SDK).<br/>
/// <b>FR:</b> Constantes des types de paquets RFXCom connus (voir SDK RFXCom).
/// </summary>
public static class PacketTypes
{
    /// <summary><b>EN/FR:</b> Interface Control (host → device). / Interface Control (hôte → device).</summary>
    public const byte InterfaceControl = 0x00;

    /// <summary><b>EN/FR:</b> Interface Message (device → host). / Interface Message (device → hôte).</summary>
    public const byte InterfaceMessage = 0x01;

    /// <summary><b>EN/FR:</b> Receiver/Transmitter message.</summary>
    public const byte ReceiverTransmitter = 0x02;

    /// <summary><b>EN/FR:</b> Lighting1 (X10, Waveman, etc.).</summary>
    public const byte Lighting1 = 0x10;

    /// <summary><b>EN/FR:</b> Lighting2 (AC/Chacon, HomeEasy EU, ANSLUT).</summary>
    public const byte Lighting2 = 0x11;

    /// <summary><b>EN/FR:</b> Lighting5 (LightwaveRF, EMW100, BBSB).</summary>
    public const byte Lighting5 = 0x14;

    /// <summary><b>EN/FR:</b> Blinds1 (Rollertrol, Hasta, etc.).</summary>
    public const byte Blinds1 = 0x19;

    /// <summary><b>EN/FR:</b> RFY (Somfy RTS).</summary>
    public const byte Rfy = 0x1A;

    /// <summary><b>EN/FR:</b> Security1 (X10 Security, motion detectors).</summary>
    public const byte Security1 = 0x20;

    /// <summary><b>EN/FR:</b> Temperature-only probe.</summary>
    public const byte Temp = 0x50;

    /// <summary><b>EN/FR:</b> Humidity-only probe.</summary>
    public const byte Humidity = 0x51;

    /// <summary><b>EN/FR:</b> Temperature + humidity probe (Oregon, Bresser, etc.).</summary>
    public const byte TempHumidity = 0x52;

    /// <summary><b>EN/FR:</b> Barometric-only probe.</summary>
    public const byte Barometric = 0x53;

    /// <summary><b>EN/FR:</b> Temperature + humidity + barometer probe.</summary>
    public const byte TempHumBaro = 0x54;

    /// <summary><b>EN/FR:</b> Rain gauge.</summary>
    public const byte Rain = 0x55;

    /// <summary><b>EN/FR:</b> Wind sensor.</summary>
    public const byte Wind = 0x56;

    /// <summary><b>EN/FR:</b> UV sensor.</summary>
    public const byte UV = 0x57;

    /// <summary><b>EN/FR:</b> Energy meter.</summary>
    public const byte Energy = 0x5A;
}

/// <summary>
/// <b>EN:</b> Subtype constants for the InterfaceControl packet (type 0x00).<br/>
/// <b>FR:</b> Constantes de sous-type pour le paquet InterfaceControl (type 0x00).
/// </summary>
public static class InterfaceCommands
{
    /// <summary><b>EN/FR:</b> Reset the device.</summary>
    public const byte Reset = 0x00;

    /// <summary><b>EN/FR:</b> Ask the device for its current status.</summary>
    public const byte GetStatus = 0x02;

    /// <summary><b>EN/FR:</b> Configure the active RF protocols.</summary>
    public const byte SetMode = 0x03;

    /// <summary><b>EN/FR:</b> Persist the current mode to non-volatile memory.</summary>
    public const byte Save = 0x06;
}

/// <summary>
/// <b>EN:</b> Somfy RFY command bytes (used in packet type 0x1A).<br/>
/// <b>FR:</b> Octets de commande Somfy RFY (paquet type 0x1A).
/// </summary>
public static class SomfyCommands
{
    /// <summary><b>EN/FR:</b> Stop (also "My" position).</summary>
    public const byte Stop = 0x00;

    /// <summary><b>EN/FR:</b> Move blind up.</summary>
    public const byte Up = 0x01;

    /// <summary><b>EN/FR:</b> Move blind down.</summary>
    public const byte Down = 0x03;

    /// <summary><b>EN/FR:</b> Pairing command (program new remote).</summary>
    public const byte Program = 0x07;

    /// <summary><b>EN/FR:</b> Combo: My + Up.</summary>
    public const byte UpStop = 0x0F;

    /// <summary><b>EN/FR:</b> Combo: My + Down.</summary>
    public const byte DownStop = 0x10;
}
