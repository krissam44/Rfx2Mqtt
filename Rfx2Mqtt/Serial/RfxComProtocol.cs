using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Models;

namespace Rfx2Mqtt.Serial;

/// <summary>
/// <b>EN:</b> Parses and builds RFXCom binary frames. Stateless except for the cyclic sequence
/// counter. <see cref="TryParse"/> drops bytes one at a time on garbage to re-sync the stream
/// on the next valid header.
/// <para>Frame layout: <c>[length] [type] [subtype] [seqnbr] [data...]</c></para>
/// <br/>
/// <b>FR:</b> Parse et construit les trames binaires RFXCom. Sans état, hormis le compteur de
/// séquence cyclique. <see cref="TryParse"/> avance d'un octet à la fois sur le bruit pour se
/// resynchroniser sur le prochain header valide.
/// <para>Format trame : <c>[length] [type] [subtype] [seqnbr] [data...]</c></para>
/// </summary>
public class RfxComProtocol
{
    private byte _sequenceNumber;
    private readonly Lock _seqLock = new();

    /// <summary>Obtient le prochain numéro de séquence (0-255, cyclique)</summary>
    public byte GetNextSequence()
    {
        lock (_seqLock)
        {
            return unchecked(++_sequenceNumber);
        }
    }

    private byte NextSequence() => GetNextSequence();

    #region Parsing

    // Les paquets RFXCom ne dépassent jamais 40 octets (le plus long fait ~28).
    // Au-delà, c'est un parasite ou une désynchronisation du flux série.
    private const int MaxPacketLength = 40;

    // Types de paquets connus pour valider le parsing
    private static readonly HashSet<byte> _knownPacketTypes =
    [
        0x00, 0x01, 0x02, 0x03,             // Interface
        0x10, 0x11, 0x12, 0x13, 0x14,       // Lighting
        0x18, 0x19, 0x1A,                    // Blinds, RFY
        0x20, 0x28,                          // Security
        0x30,                                // Camera
        0x40, 0x41, 0x42, 0x44, 0x45,       // Thermostat
        0x50, 0x51, 0x52, 0x53, 0x54,       // Temp, Humidity, TempHum, Baro, TempHumBaro
        0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, // Rain, Wind, UV, DateTime, Current, Energy
        0x5B, 0x5C, 0x5D,                   // CurrentEnergy, Power, Weight
        0x70, 0x71, 0x72                     // RFXSensor, RFXMeter
    ];

    /// <summary>
    /// Tente de parser un paquet à partir d'un buffer.
    /// Retourne le paquet parsé et le nombre d'octets consommés.
    ///
    /// Si la longueur ou le type sont invalides, on avance d'un seul octet
    /// pour tenter de se re-synchroniser sur le prochain paquet valide.
    /// </summary>
    public (RfxComPacket? Packet, int BytesConsumed) TryParse(byte[] buffer, int offset, int count)
    {
        if (count < 1)
            return (null, 0);

        byte length = buffer[offset];

        // Paquet de longueur 0 = à ignorer (flush après reset)
        if (length == 0)
            return (null, 1);

        // Longueur aberrante → parasite, on saute 1 octet pour re-sync
        if (length > MaxPacketLength)
            return (null, 1);

        // Paquet trop court pour être valide (min: type + subtype + seqnbr = 3)
        if (length < 3)
            return (null, 1);

        // On a besoin de length + 1 octets au total
        int totalLength = length + 1;
        if (count < totalLength)
            return (null, 0); // Pas encore assez de données, on attend

        // Vérifier que le type de paquet est connu
        byte packetType = buffer[offset + 1];
        if (!_knownPacketTypes.Contains(packetType))
        {
            // Type inconnu → probablement désynchronisé, on saute 1 octet
            return (null, 1);
        }

        // Vérifier la cohérence du sous-type pour les types les plus courants.
        // Cela évite d'accepter des données RF parasites (ex: A-OK, Somfy) dont les bytes
        // s'alignent par hasard avec un type connu.
        byte subType = buffer[offset + 2];
        if (!IsSubTypeValid(packetType, subType))
        {
            return (null, 1);
        }

        var rawBytes = new byte[totalLength];
        Array.Copy(buffer, offset, rawBytes, 0, totalLength);

        var packet = new RfxComPacket
        {
            Length = length,
            PacketType = packetType,
            SubType = buffer[offset + 2],
            SequenceNumber = buffer[offset + 3],
            RawBytes = rawBytes,
            ReceivedAt = DateTime.UtcNow
        };

        // Data = tout ce qui suit le seqnbr
        int dataLength = length - 3;
        if (dataLength > 0)
        {
            packet.Data = new byte[dataLength];
            Array.Copy(buffer, offset + 4, packet.Data, 0, dataLength);
        }

        return (packet, totalLength);
    }

    /// <summary>
    /// Vérifie que le sous-type est cohérent avec le type de paquet.
    /// Filtre les faux positifs causés par des données RF brutes (A-OK, Somfy, etc.)
    /// dont les bytes s'alignent par hasard avec un en-tête valide.
    /// </summary>
    private static bool IsSubTypeValid(byte packetType, byte subType) => packetType switch
    {
        // Interface Control (émis par le host, jamais reçu — sécurité)
        0x00 => subType <= 0x10,

        // Interface Message (réponse du RFXCom) : subtypes 0x00-0x05 et 0x10
        0x01 => subType <= 0x05 || subType == 0x10,

        // Undecoded : subtypes 0x00-0x11 documentés
        0x03 => subType <= 0x11,

        // Pour tous les autres types (capteurs, éclairage, sécurité, etc.)
        // les subtypes vont de 0x00 à ~0x20 au maximum
        _ => subType <= 0x20
    };

    #endregion

    #region Commandes Interface (type 0x00)

    /// <summary>
    /// Construit la commande Reset.
    /// Le reset nécessite un paquet de 14 octets (length=13) rempli de zéros.
    /// </summary>
    public byte[] BuildReset()
    {
        var cmd = new byte[14];
        cmd[0] = 13; // length
        // Tout le reste à 0x00 (type=0x00, subtype=0x00 = Reset)
        return cmd;
    }

    /// <summary>
    /// Construit la commande Get Status.
    /// </summary>
    public byte[] BuildGetStatus()
    {
        var cmd = new byte[14];
        cmd[0] = 13;                       // length
        cmd[1] = PacketTypes.InterfaceControl; // type 0x00
        cmd[2] = 0x00;                     // subtype
        cmd[3] = NextSequence();           // seqnbr
        cmd[4] = InterfaceCommands.GetStatus; // cmnd = 0x02
        // Le reste à 0x00
        return cmd;
    }

    /// <summary>
    /// Construit la commande Set Mode pour activer les protocoles configurés.
    /// </summary>
    public byte[] BuildSetMode(RfxComProtocols protocols, byte frequency = 0x53)
    {
        var cmd = new byte[14];
        cmd[0] = 13;                            // length
        cmd[1] = PacketTypes.InterfaceControl;   // type 0x00
        cmd[2] = 0x00;                          // subtype
        cmd[3] = NextSequence();                // seqnbr
        cmd[4] = InterfaceCommands.SetMode;     // cmnd = 0x03
        cmd[5] = frequency;                     // msg1: 0x53 = 433.92MHz
        cmd[6] = 0x00;                          // msg2: firmware type (laisser 0)
        cmd[7] = protocols.ToMsg3();            // msg3: protocoles set 1
        cmd[8] = protocols.ToMsg4();            // msg4: protocoles set 2
        cmd[9] = protocols.ToMsg5();            // msg5: protocoles set 3
        // msg6..msg9 = 0x00
        return cmd;
    }

    #endregion

    #region Commande Somfy RFY (type 0x1A)

    /// <summary>
    /// Construit une commande Somfy RFY.
    /// </summary>
    /// <param name="subType">0x00 = RFY, 0x01 = RFY Ext</param>
    /// <param name="id">ID de l'émetteur (3 octets, ex: 0x01, 0x02, 0x03)</param>
    /// <param name="unitCode">Code unité (0x00 = all, 0x01-0x04)</param>
    /// <param name="command">Commande (voir SomfyCommands)</param>
    public byte[] BuildSomfyCommand(byte subType, byte[] id, byte unitCode, byte command)
    {
        if (id.Length != 3)
            throw new ArgumentException("L'ID Somfy doit faire 3 octets", nameof(id));

        var cmd = new byte[13];
        cmd[0] = 12;                  // length
        cmd[1] = PacketTypes.Rfy;     // type 0x1A
        cmd[2] = subType;             // subtype
        cmd[3] = NextSequence();      // seqnbr
        cmd[4] = id[0];              // id1
        cmd[5] = id[1];              // id2
        cmd[6] = id[2];              // id3
        cmd[7] = unitCode;           // unit code
        cmd[8] = command;            // command
        // cmd[9..12] = 0x00 (rfu)
        return cmd;
    }

    #endregion

    #region Commande Lighting2 (type 0x11)

    /// <summary>
    /// Construit une trame Lighting2 (type 0x11) pour Chacon/DIO/HomeEasy EU.
    /// Taille : 12 octets (length=11).
    /// </summary>
    /// <param name="subType">0x00 = AC/Chacon, 0x01 = HomeEasy EU, 0x02 = ANSLUT</param>
    /// <param name="id">ID de l'émetteur (4 octets)</param>
    /// <param name="unitCode">Code unité (1-16, 0 = group)</param>
    /// <param name="command">Commande (voir Lighting2Commands)</param>
    /// <param name="level">Niveau dimmer (0-15)</param>
    public byte[] BuildLighting2Command(byte subType, byte[] id, byte unitCode, byte command, byte level)
    {
        if (id.Length != 4)
            throw new ArgumentException("L'ID Lighting2 doit faire 4 octets", nameof(id));

        var cmd = new byte[12];
        cmd[0] = 11;                       // length
        cmd[1] = PacketTypes.Lighting2;    // type 0x11
        cmd[2] = subType;                  // 0x00 = AC/Chacon
        cmd[3] = NextSequence();           // seqnbr
        cmd[4] = id[0];                   // id1
        cmd[5] = id[1];                   // id2
        cmd[6] = id[2];                   // id3
        cmd[7] = id[3];                   // id4
        cmd[8] = unitCode;                // unit code
        cmd[9] = command;                  // cmnd
        cmd[10] = level;                   // level (0-15)
        cmd[11] = 0x00;                    // filler + rssi
        return cmd;
    }

    #endregion

    #region Helpers

    /// <summary>Formate une trame en hex lisible pour les logs</summary>
    public static string FormatHex(byte[] data)
        => string.Join(" ", data.Select(b => $"{b:X2}"));

    #endregion
}
