namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> Strongly-typed binding of the <c>RfxCom</c> section in <c>appsettings.json</c>.
/// Holds all technical settings driving the bridge: serial port, RFXCom init behavior, MQTT
/// reconnection, availability tracking and the bitmask of enabled RF protocols.
/// Device inventories are <i>not</i> stored here anymore — see <see cref="IDeviceRepository"/>
/// (file <c>data/devices.yaml</c>).
/// <br/>
/// <b>FR:</b> Liaison typée de la section <c>RfxCom</c> dans <c>appsettings.json</c>.
/// Contient les paramètres techniques du bridge : port série, init RFXCom, reconnexion MQTT,
/// suivi de disponibilité, bitmask des protocoles RF activés.
/// Les inventaires d'appareils ne sont <i>plus</i> stockés ici — voir <see cref="IDeviceRepository"/>
/// (fichier <c>data/devices.yaml</c>).
/// </summary>
public class RfxComOptions
{
    /// <summary>
    /// <b>EN:</b> Configuration section name (binding key).<br/>
    /// <b>FR:</b> Nom de la section de configuration (clé de liaison).
    /// </summary>
    public const string SectionName = "RfxCom";

    /// <summary>
    /// <b>EN:</b> RFXCom serial port. Linux: <c>/dev/ttyUSB0</c>. Windows: <c>COM3</c>, etc.<br/>
    /// <b>FR:</b> Port série du RFXCom. Linux : <c>/dev/ttyUSB0</c>. Windows : <c>COM3</c>, etc.
    /// </summary>
    public string PortName { get; set; } = "/dev/ttyUSB0";

    /// <summary>
    /// <b>EN:</b> Serial baud rate. Always 38400 for RFXCom — do not change.<br/>
    /// <b>FR:</b> Vitesse de communication série. Toujours 38400 pour RFXCom — ne pas modifier.
    /// </summary>
    public int BaudRate { get; set; } = 38400;

    /// <summary>
    /// <b>EN:</b> Delay (ms) after sending Reset before the device is usable. Minimum 1000.<br/>
    /// <b>FR:</b> Délai (ms) après envoi du Reset avant que le RFXCom soit utilisable. Minimum 1000.
    /// </summary>
    public int ResetDelayMs { get; set; } = 1500;

    /// <summary>
    /// <b>EN:</b> Serial read timeout (ms).<br/>
    /// <b>FR:</b> Timeout de lecture série (ms).
    /// </summary>
    public int ReadTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// <b>EN:</b> Delay between reconnection attempts on connection loss (seconds).<br/>
    /// <b>FR:</b> Délai entre tentatives de reconnexion sur perte de connexion (secondes).
    /// </summary>
    public int ReconnectDelaySec { get; set; } = 10;

    /// <summary>
    /// <b>EN:</b> When <c>true</c>, the Worker does NOT alter the RFXCom protocol config at startup.
    /// Useful to keep a configuration done with the official RFXmngr utility.<br/>
    /// <b>FR:</b> Si <c>true</c>, le Worker ne modifie pas la config des protocoles au démarrage.
    /// Utile pour conserver une configuration faite via l'utilitaire officiel RFXmngr.
    /// </summary>
    public bool SkipSetMode { get; set; } = false;

    /// <summary>
    /// <b>EN:</b> Initial value of <see cref="PermitJoinState"/>. When enabled, every detected
    /// device is published to MQTT (zigbee2mqtt-style discovery mode). When disabled, only
    /// devices listed in <c>data/devices.yaml</c> are published. Toggled at runtime via
    /// <c>{prefix}/command/permit_join</c> and persisted back to this file.<br/>
    /// <b>FR:</b> Valeur initiale de <see cref="PermitJoinState"/>. Si activé, tout appareil détecté
    /// est publié en MQTT (mode découverte à la zigbee2mqtt). Sinon, seuls les appareils déclarés
    /// dans <c>data/devices.yaml</c> sont publiés. Basculable au runtime via
    /// <c>{prefix}/command/permit_join</c> et persisté dans ce fichier.
    /// </summary>
    public bool PermitJoin { get; set; } = true;

    /// <summary>
    /// <b>EN:</b> Seconds without a frame before a sensor is flagged offline. Oregon/Bresser
    /// probes emit every 30–90 s, so 300 s (5 min) is a safe default.<br/>
    /// <b>FR:</b> Secondes sans trame avant de considérer une sonde comme offline. Les sondes
    /// Oregon/Bresser émettent toutes les 30–90 s, 300 s (5 min) est un défaut sûr.
    /// </summary>
    public int AvailabilityTimeoutSec { get; set; } = 300;

    /// <summary>
    /// <b>EN:</b> How often (seconds) the availability loop scans sensors.<br/>
    /// <b>FR:</b> Fréquence (secondes) à laquelle la boucle d'availability parcourt les capteurs.
    /// </summary>
    public int AvailabilityCheckIntervalSec { get; set; } = 60;

    /// <summary>
    /// <b>EN:</b> Seconds after a motion event before publishing an automatic <c>OFF</c>.
    /// Required for X10 Security sensors that never send an explicit <c>no_motion</c> frame.
    /// Set to 0 to disable auto-reset.<br/>
    /// <b>FR:</b> Secondes après une détection de mouvement avant publication automatique d'un
    /// <c>OFF</c>. Indispensable pour les capteurs X10 Security qui n'envoient jamais de trame
    /// <c>no_motion</c> explicite. 0 = désactiver l'auto-reset.
    /// </summary>
    public int MotionClearDelaySec { get; set; } = 120;

    /// <summary>
    /// <b>EN:</b> [LEGACY] Oregon/Bresser/Cresta probes. Migrated to <c>data/devices.yaml</c> on
    /// first startup; this property remains only as input for the one-shot migration.<br/>
    /// <b>FR:</b> [LEGACY] Sondes Oregon/Bresser/Cresta. Migrées vers <c>data/devices.yaml</c> au
    /// premier démarrage ; cette propriété ne sert plus qu'en entrée de la migration unique.
    /// </summary>
    public List<OregonDevice> OregonDevices { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> [LEGACY] X10 Security sensors. Migrated to <c>data/devices.yaml</c>.<br/>
    /// <b>FR:</b> [LEGACY] Capteurs Security X10. Migrés vers <c>data/devices.yaml</c>.
    /// </summary>
    public List<SecurityDevice> SecurityDevices { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> [LEGACY] Somfy RTS blinds. Migrated to <c>data/devices.yaml</c>.<br/>
    /// <b>FR:</b> [LEGACY] Volets Somfy RTS. Migrés vers <c>data/devices.yaml</c>.
    /// </summary>
    public List<SomfyDevice> SomfyDevices { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> [LEGACY] Chacon/DIO devices. Migrated to <c>data/devices.yaml</c>.<br/>
    /// <b>FR:</b> [LEGACY] Équipements Chacon/DIO. Migrés vers <c>data/devices.yaml</c>.
    /// </summary>
    public List<ChaconDevice> ChaconDevices { get; set; } = [];

    /// <summary>
    /// <b>EN:</b> RF protocols to activate at boot via the RFXCom <c>Set Mode</c> command.<br/>
    /// <b>FR:</b> Protocoles RF à activer au démarrage via la commande <c>Set Mode</c> du RFXCom.
    /// </summary>
    public RfxComProtocols Protocols { get; set; } = new();
}

/// <summary>
/// <b>EN:</b> Activation bits for RF protocols, packed into bytes msg3/msg4/msg5 of the RFXCom
/// <c>Set Mode</c> frame. Each boolean property maps to a single bit. See the official RFXCom
/// SDK PDF for the canonical mapping.<br/>
/// <b>FR:</b> Bits d'activation des protocoles RF, encodés dans les octets msg3/msg4/msg5 de la
/// trame RFXCom <c>Set Mode</c>. Chaque booléen mappe sur un bit. Voir le PDF du SDK RFXCom pour
/// la correspondance officielle.
/// </summary>
public class RfxComProtocols
{
    // ── Byte msg3 ───────────────────────────────────────────────────────────────
    /// <summary>
    /// <b>EN:</b> Emit raw undecoded RF frames (type 0x03). PRODUCTION: keep false — masks
    /// properly decoded packets. Only for reverse-engineering unknown protocols.<br/>
    /// <b>FR:</b> Émettre les trames RF brutes non décodées (type 0x03). PRODUCTION : laisser
    /// false — masque les paquets correctement décodés. À n'utiliser que pour faire du
    /// reverse-engineering sur des protocoles inconnus.
    /// </summary>
    public bool Undecoded { get; set; } = false;

    /// <summary><b>EN/FR:</b> Reserved bit. Leave <c>false</c>. / Bit réservé, laisser <c>false</c>.</summary>
    public bool RFU1 { get; set; } = false;

    /// <summary><b>EN/FR:</b> Byron SX doorbells.</summary>
    public bool ByronSX { get; set; } = false;

    /// <summary><b>EN/FR:</b> RSL (Conrad) lighting.</summary>
    public bool RSL { get; set; } = false;

    /// <summary>
    /// <b>EN:</b> Lighting4 (PT2262-based remotes). Also required for X10 Security Motion
    /// sensors (paired with <see cref="X10"/>).<br/>
    /// <b>FR:</b> Lighting4 (télécommandes PT2262). Également requis pour les capteurs X10
    /// Security Motion (en complément de <see cref="X10"/>).
    /// </summary>
    public bool Lighting4 { get; set; } = false;

    /// <summary><b>EN/FR:</b> Fine Offset / Viking weather probes.</summary>
    public bool FineOffsetViking { get; set; } = false;

    /// <summary><b>EN/FR:</b> Rubicson temperature probes.</summary>
    public bool Rubicson { get; set; } = false;

    /// <summary><b>EN/FR:</b> AE Blyss switches.</summary>
    public bool AEBlyss { get; set; } = false;

    // ── Byte msg4 ───────────────────────────────────────────────────────────────
    /// <summary><b>EN/FR:</b> Blinds T1–T4 (BFT, Somfy non-RTS, etc.).</summary>
    public bool BlindsT1T2T3T4 { get; set; } = false;

    /// <summary><b>EN/FR:</b> Blinds T0.</summary>
    public bool BlindsT0 { get; set; } = false;

    /// <summary><b>EN/FR:</b> ProGuard security.</summary>
    public bool ProGuard { get; set; } = false;

    /// <summary><b>EN/FR:</b> FS20 (German home automation).</summary>
    public bool FS20 { get; set; } = false;

    /// <summary><b>EN/FR:</b> La Crosse weather probes.</summary>
    public bool LaCrosse { get; set; } = false;

    /// <summary><b>EN/FR:</b> Hideki weather probes.</summary>
    public bool Hideki { get; set; } = false;

    /// <summary><b>EN/FR:</b> LightwaveRF lighting.</summary>
    public bool LightwaveRF { get; set; } = false;

    /// <summary><b>EN/FR:</b> Mertik thermostats (gas fireplaces).</summary>
    public bool Mertik { get; set; } = false;

    // ── Byte msg5 ───────────────────────────────────────────────────────────────
    /// <summary><b>EN/FR:</b> Visonic security.</summary>
    public bool Visonic { get; set; } = false;

    /// <summary><b>EN/FR:</b> ATI remotes (Multimedia).</summary>
    public bool ATI { get; set; } = false;

    /// <summary>
    /// <b>EN:</b> Oregon Scientific weather probes. Default: <c>true</c>.<br/>
    /// <b>FR:</b> Sondes Oregon Scientific. Défaut : <c>true</c>.
    /// </summary>
    public bool OregonScientific { get; set; } = true;

    /// <summary><b>EN/FR:</b> Meiantech security.</summary>
    public bool Meiantech { get; set; } = false;

    /// <summary><b>EN/FR:</b> HomeEasy EU lighting.</summary>
    public bool HomeEasyEU { get; set; } = false;

    /// <summary><b>EN/FR:</b> AC (Chacon/DIO Lighting2).</summary>
    public bool AC { get; set; } = false;

    /// <summary><b>EN/FR:</b> ARC (HomeEasy UK, KlikAanKlikUit, Chacon).</summary>
    public bool ARC { get; set; } = false;

    /// <summary>
    /// <b>EN:</b> X10 lighting (and required for X10 Security Motion, paired with
    /// <see cref="Lighting4"/>).<br/>
    /// <b>FR:</b> X10 lighting (et nécessaire pour les X10 Security Motion, en complément de
    /// <see cref="Lighting4"/>).
    /// </summary>
    public bool X10 { get; set; } = false;

    /// <summary>
    /// <b>EN:</b> Pack the msg3 byte from active flags.<br/>
    /// <b>FR:</b> Encode l'octet msg3 à partir des flags actifs.
    /// </summary>
    public byte ToMsg3()
    {
        byte b = 0;
        if (Undecoded)       b |= 0x80;
        if (RFU1)            b |= 0x40;
        if (ByronSX)         b |= 0x20;
        if (RSL)             b |= 0x10;
        if (Lighting4)       b |= 0x08;
        if (FineOffsetViking) b |= 0x04;
        if (Rubicson)        b |= 0x02;
        if (AEBlyss)         b |= 0x01;
        return b;
    }

    /// <summary>
    /// <b>EN:</b> Pack the msg4 byte from active flags.<br/>
    /// <b>FR:</b> Encode l'octet msg4 à partir des flags actifs.
    /// </summary>
    public byte ToMsg4()
    {
        byte b = 0;
        if (BlindsT1T2T3T4) b |= 0x80;
        if (BlindsT0)       b |= 0x40;
        if (ProGuard)        b |= 0x20;
        if (FS20)            b |= 0x10;
        if (LaCrosse)        b |= 0x08;
        if (Hideki)          b |= 0x04;
        if (LightwaveRF)     b |= 0x02;
        if (Mertik)          b |= 0x01;
        return b;
    }

    /// <summary>
    /// <b>EN:</b> Pack the msg5 byte from active flags.<br/>
    /// <b>FR:</b> Encode l'octet msg5 à partir des flags actifs.
    /// </summary>
    public byte ToMsg5()
    {
        byte b = 0;
        if (Visonic)         b |= 0x80;
        if (ATI)             b |= 0x40;
        if (OregonScientific) b |= 0x20;
        if (Meiantech)       b |= 0x10;
        if (HomeEasyEU)      b |= 0x08;
        if (AC)              b |= 0x04;
        if (ARC)             b |= 0x02;
        if (X10)             b |= 0x01;
        return b;
    }
}

/// <summary>
/// <b>EN:</b> Strongly-typed binding of the <c>Mqtt</c> section in <c>appsettings.json</c>.<br/>
/// <b>FR:</b> Liaison typée de la section <c>Mqtt</c> dans <c>appsettings.json</c>.
/// </summary>
public class MqttOptions
{
    /// <summary><b>EN/FR:</b> Configuration section name. / Nom de section.</summary>
    public const string SectionName = "Mqtt";

    /// <summary>
    /// <b>EN:</b> MQTT broker hostname or IP. On Linux/systemd, prefer the IP address — DNS
    /// resolution can fail silently before the network stack is fully up.<br/>
    /// <b>FR:</b> Hôte ou IP du broker MQTT. Sur Linux/systemd, préférer l'IP — la résolution DNS
    /// peut échouer silencieusement avant que la pile réseau soit complètement prête.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary><b>EN/FR:</b> Broker TCP port. / Port TCP du broker.</summary>
    public int Port { get; set; } = 1883;

    /// <summary><b>EN/FR:</b> MQTT username (optional). / Identifiant MQTT (optionnel).</summary>
    public string? Username { get; set; }

    /// <summary><b>EN/FR:</b> MQTT password (optional). / Mot de passe MQTT (optionnel).</summary>
    public string? Password { get; set; }

    /// <summary>
    /// <b>EN:</b> MQTT client identifier (must be unique on the broker).<br/>
    /// <b>FR:</b> Identifiant client MQTT (doit être unique sur le broker).
    /// </summary>
    public string ClientId { get; set; } = "Rfx2Mqtt";

    /// <summary>
    /// <b>EN:</b> Root prefix prepended to every topic the bridge publishes/subscribes.<br/>
    /// <b>FR:</b> Préfixe racine ajouté à tous les topics publiés/souscrits par le bridge.
    /// </summary>
    public string TopicPrefix { get; set; } = "rfxcom";

    /// <summary>
    /// <b>EN:</b> Delay between MQTT reconnection attempts (seconds).<br/>
    /// <b>FR:</b> Délai entre tentatives de reconnexion MQTT (secondes).
    /// </summary>
    public int ReconnectDelaySec { get; set; } = 5;

    /// <summary>
    /// <b>EN:</b> Interval (seconds) between active MQTT PINGREQ probes. Detects "zombie"
    /// connections where the broker stops responding without the TCP socket dropping (the
    /// keep-alive alone does not always catch this). 0 = disabled.<br/>
    /// <b>FR:</b> Intervalle (secondes) entre les pings MQTT actifs (PINGREQ). Détecte les
    /// connexions "zombies" où le broker ne répond plus sans que le socket TCP tombe (le
    /// keep-alive seul ne l'attrape pas toujours). 0 = désactivé.
    /// </summary>
    public int PingIntervalSec { get; set; } = 60;

    /// <summary>
    /// <b>EN:</b> Timeout (seconds) for one PINGREQ before the connection is declared dead and
    /// a reconnection is forced.<br/>
    /// <b>FR:</b> Timeout (secondes) d'un PINGREQ avant de déclarer la connexion morte et de
    /// forcer une reconnexion.
    /// </summary>
    public int PingTimeoutSec { get; set; } = 10;
}

/// <summary>
/// <b>EN:</b> Configured Oregon / Bresser / Cresta temperature-humidity probe. The hex ID is
/// printed in the logs the first time the device is heard (or visible in MQTT Explorer).<br/>
/// <b>FR:</b> Sonde T°/Humidité Oregon / Bresser / Cresta configurée. L'ID hex apparaît dans
/// les logs à la première trame reçue (ou est visible dans MQTT Explorer).
/// </summary>
public class OregonDevice
{
    /// <summary>
    /// <b>EN:</b> Friendly name used in the MQTT topic instead of the raw hex ID.<br/>
    /// <b>FR:</b> Nom amical utilisé dans le topic MQTT à la place de l'ID hex brut.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> Probe hex ID (e.g. <c>"0x710E"</c>).<br/>
    /// <b>FR:</b> ID hex de la sonde (ex : <c>"0x710E"</c>).
    /// </summary>
    public string Id { get; set; } = "";
}

/// <summary>
/// <b>EN:</b> Configured X10 Security sensor (motion detector, door/window contact, etc.).<br/>
/// <b>FR:</b> Capteur Security X10 configuré (détecteur de mouvement, contact porte/fenêtre, etc.).
/// </summary>
public class SecurityDevice
{
    /// <summary><b>EN:</b> Friendly name. <b>FR:</b> Nom amical.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> Sensor hex ID (e.g. <c>"0x831480"</c>).<br/>
    /// <b>FR:</b> ID hex du capteur (ex : <c>"0x831480"</c>).
    /// </summary>
    public string Id { get; set; } = "";
}

/// <summary>
/// <b>EN:</b> Parses hex device IDs in the formats <c>"AB CD EF"</c> or <c>"0xAB 0xCD 0xEF"</c>
/// into a byte array of the expected length. Throws <see cref="FormatException"/> on bad input.<br/>
/// <b>FR:</b> Parse les IDs hex de devices aux formats <c>"AB CD EF"</c> ou
/// <c>"0xAB 0xCD 0xEF"</c> en tableau d'octets de la longueur attendue. Lève
/// <see cref="FormatException"/> sur entrée invalide.
/// </summary>
internal static class HexIdParser
{
    /// <summary>
    /// <b>EN:</b> Parse <paramref name="id"/> into exactly <paramref name="expectedBytes"/> bytes.<br/>
    /// <b>FR:</b> Parse <paramref name="id"/> en exactement <paramref name="expectedBytes"/> octets.
    /// </summary>
    /// <param name="id"><b>EN/FR:</b> Source string. / Chaîne source.</param>
    /// <param name="expectedBytes"><b>EN/FR:</b> Expected byte count. / Nombre d'octets attendu.</param>
    /// <param name="contextName"><b>EN:</b> Device kind for error message ("Somfy", "Chacon", …).
    /// <b>FR:</b> Type d'appareil pour le message d'erreur.</param>
    public static byte[] Parse(string id, int expectedBytes, string contextName)
    {
        var parts = id.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                      .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedBytes)
            throw new FormatException(
                $"L'ID {contextName} '{id}' doit contenir {expectedBytes} octets hex séparés par des espaces");
        var bytes = new byte[expectedBytes];
        for (int i = 0; i < expectedBytes; i++)
            bytes[i] = Convert.ToByte(parts[i], 16);
        return bytes;
    }
}

/// <summary>
/// <b>EN:</b> Configured Somfy RTS blind. Bytes cached after first parse for performance —
/// see <see cref="GetIdBytes"/>.<br/>
/// <b>FR:</b> Volet Somfy RTS configuré. Bytes mis en cache après le premier parsing pour la
/// performance — voir <see cref="GetIdBytes"/>.
/// </summary>
public class SomfyDevice
{
    private byte[]? _idBytesCache;
    private string? _idBytesCachedFor;

    /// <summary><b>EN:</b> Friendly name. <b>FR:</b> Nom amical.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> 3-byte hex ID, space-separated (e.g. <c>"0 01 01"</c>).<br/>
    /// <b>FR:</b> ID hex sur 3 octets, séparés par des espaces (ex : <c>"0 01 01"</c>).
    /// </summary>
    public string Id { get; set; } = "00 00 00";

    /// <summary>
    /// <b>EN:</b> Unit code (1–4, or 0 for "all").<br/>
    /// <b>FR:</b> Code d'unité (1–4, ou 0 pour "tous").
    /// </summary>
    public byte UnitCode { get; set; } = 1;

    /// <summary>
    /// <b>EN:</b> RFY subtype (0 = standard RFY, 1 = RFY Ext).<br/>
    /// <b>FR:</b> Sous-type RFY (0 = RFY standard, 1 = RFY Ext).
    /// </summary>
    public byte SubType { get; set; } = 0;

    /// <summary>
    /// <b>EN:</b> Returns the cached byte representation of <see cref="Id"/>; re-parses on Id change.<br/>
    /// <b>FR:</b> Renvoie la représentation en octets de <see cref="Id"/> mise en cache ;
    /// re-parse si Id change.
    /// </summary>
    public byte[] GetIdBytes()
    {
        if (_idBytesCache is not null && ReferenceEquals(_idBytesCachedFor, Id))
            return _idBytesCache;
        var bytes = HexIdParser.Parse(Id, 3, "Somfy");
        _idBytesCache = bytes;
        _idBytesCachedFor = Id;
        return bytes;
    }
}

/// <summary>
/// <b>EN:</b> Configured Chacon / DIO / HomeEasy EU device. Bytes cached — see <see cref="GetIdBytes"/>.<br/>
/// <b>FR:</b> Équipement Chacon / DIO / HomeEasy EU configuré. Bytes mis en cache — voir
/// <see cref="GetIdBytes"/>.
/// </summary>
public class ChaconDevice
{
    private byte[]? _idBytesCache;
    private string? _idBytesCachedFor;

    /// <summary><b>EN:</b> Friendly name. <b>FR:</b> Nom amical.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// <b>EN:</b> 4-byte hex ID, space-separated (e.g. <c>"01 9E 75 0E"</c>).<br/>
    /// <b>FR:</b> ID hex sur 4 octets, séparés par des espaces (ex : <c>"01 9E 75 0E"</c>).
    /// </summary>
    public string Id { get; set; } = "00 00 00 00";

    /// <summary>
    /// <b>EN:</b> Unit code (1–16, or 0 for group).<br/>
    /// <b>FR:</b> Code d'unité (1–16, ou 0 pour un groupe).
    /// </summary>
    public byte UnitCode { get; set; } = 1;

    /// <summary>
    /// <b>EN:</b> Lighting2 subtype (0 = AC/Chacon, 1 = HomeEasy EU, 2 = ANSLUT).<br/>
    /// <b>FR:</b> Sous-type Lighting2 (0 = AC/Chacon, 1 = HomeEasy EU, 2 = ANSLUT).
    /// </summary>
    public byte SubType { get; set; } = 0;

    /// <summary>
    /// <b>EN:</b> Cached byte representation of <see cref="Id"/>; re-parses on Id change.<br/>
    /// <b>FR:</b> Représentation en octets de <see cref="Id"/> mise en cache ; re-parse si Id change.
    /// </summary>
    public byte[] GetIdBytes()
    {
        if (_idBytesCache is not null && ReferenceEquals(_idBytesCachedFor, Id))
            return _idBytesCache;
        var bytes = HexIdParser.Parse(Id, 4, "Chacon");
        _idBytesCache = bytes;
        _idBytesCachedFor = Id;
        return bytes;
    }
}
