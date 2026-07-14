namespace Rfx2Mqtt.Mqtt;

/// <summary>
/// <b>EN:</b> Centralizes MQTT topic construction so handlers don't sprinkle magic strings.
/// <para>Layout / Schéma :</para>
/// <list type="bullet">
///   <item><c>{prefix}/availability</c> — bridge LWT</item>
///   <item><c>{prefix}/config/permit_join</c> — discovery-mode state</item>
///   <item><c>{prefix}/command/#</c> — command topics (subscribe)</item>
///   <item><c>{prefix}/sensor/{kind}/{name}</c> — full JSON</item>
///   <item><c>{prefix}/sensor/{kind}/{name}/availability</c> — online/offline per sensor</item>
///   <item><c>{prefix}/sensor/{kind}/{name}/{attribute}</c> — individual value</item>
///   <item><c>{prefix}/event/{kind}/{name}</c> — one-shot events (not retained)</item>
/// </list>
/// <br/>
/// <b>FR:</b> Centralise la construction des topics MQTT pour éviter la duplication de magic
/// strings dans les handlers.
/// </summary>
public class MqttTopics
{
    /// <summary>
    /// <b>EN:</b> Root topic prefix (e.g. <c>"rfxcom"</c>).<br/>
    /// <b>FR:</b> Préfixe racine des topics (ex : <c>"rfxcom"</c>).
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// <b>EN:</b> Constructor — wraps an immutable prefix.<br/>
    /// <b>FR:</b> Constructeur — encapsule un préfixe immuable.
    /// </summary>
    public MqttTopics(string prefix) { Prefix = prefix; }

    /// <summary>
    /// <b>EN:</b> <c>{prefix}/availability</c> — bridge online/offline LWT topic.<br/>
    /// <b>FR:</b> <c>{prefix}/availability</c> — topic LWT online/offline du bridge.
    /// </summary>
    public string BridgeAvailability => $"{Prefix}/availability";

    /// <summary>
    /// <b>EN:</b> <c>{prefix}/config/permit_join</c> — discovery mode state (retained).<br/>
    /// <b>FR:</b> <c>{prefix}/config/permit_join</c> — état du mode découverte (retained).
    /// </summary>
    public string PermitJoinState => $"{Prefix}/config/permit_join";

    /// <summary>
    /// <b>EN:</b> <c>{prefix}/devices</c> — retained JSON array of all configured devices with
    /// their MQTT topics and Matter device type. Consumed by Matter bridges (e.g. MatterBridge)
    /// to register bridged devices dynamically.<br/>
    /// <b>FR:</b> <c>{prefix}/devices</c> — tableau JSON retained de tous les appareils configurés
    /// avec leurs topics MQTT et leur type Matter. Consommé par les bridges Matter (ex. MatterBridge)
    /// pour enregistrer dynamiquement les appareils.
    /// </summary>
    public string Devices => $"{Prefix}/devices";

    /// <summary>
    /// <b>EN:</b> <c>{prefix}/info</c> — retained bridge identity (name, version, release date,
    /// copyright, RFXCom firmware). Published at startup, zigbee2mqtt-style.<br/>
    /// <b>FR:</b> <c>{prefix}/info</c> — identité du bridge (nom, version, date de release,
    /// copyright, firmware RFXCom), retained. Publié au démarrage, à la zigbee2mqtt.
    /// </summary>
    public string BridgeInfo => $"{Prefix}/info";

    /// <summary>
    /// <b>EN:</b> <c>{prefix}/command/#</c> — wildcard for all inbound commands.<br/>
    /// <b>FR:</b> <c>{prefix}/command/#</c> — wildcard de toutes les commandes entrantes.
    /// </summary>
    public string CommandWildcard => $"{Prefix}/command/#";

    /// <summary>
    /// <b>EN:</b> Sensor base topic carrying the full JSON payload.<br/>
    /// <b>FR:</b> Topic de base du capteur portant le JSON complet.
    /// </summary>
    public string SensorBase(string kind, string name) => $"{Prefix}/sensor/{kind}/{name}";

    /// <summary>
    /// <b>EN:</b> Availability sub-topic for a given sensor.<br/>
    /// <b>FR:</b> Sous-topic d'availability pour un capteur donné.
    /// </summary>
    public string SensorAvailability(string kind, string name) => $"{SensorBase(kind, name)}/availability";

    /// <summary>
    /// <b>EN:</b> Sub-topic carrying a single attribute (e.g. <c>temperature</c>, <c>battery</c>).<br/>
    /// <b>FR:</b> Sous-topic portant un attribut unique (ex : <c>temperature</c>, <c>battery</c>).
    /// </summary>
    public string SensorAttribute(string kind, string name, string attribute) => $"{SensorBase(kind, name)}/{attribute}";

    /// <summary>
    /// <b>EN:</b> Event topic (typically used for Somfy remote button presses).<br/>
    /// <b>FR:</b> Topic d'événement (typiquement pour les appuis sur télécommande Somfy).
    /// </summary>
    public string Event(string kind, string name) => $"{Prefix}/event/{kind}/{name}";

    // ── EN/FR: kind constants (segment right after /sensor/ or /event/) ─────────
    /// <summary><b>EN/FR:</b> Temperature/humidity probe kind.</summary>
    public const string KindTh = "th";
    /// <summary><b>EN/FR:</b> Temperature/humidity/barometer probe kind.</summary>
    public const string KindThBaro = "thb";
    /// <summary><b>EN/FR:</b> Security sensor kind.</summary>
    public const string KindSecurity = "security";
    /// <summary><b>EN/FR:</b> Chacon/Lighting2 device kind.</summary>
    public const string KindChacon = "chacon";
    /// <summary><b>EN/FR:</b> Somfy blind kind.</summary>
    public const string KindSomfy = "somfy";

    // ── EN/FR: common attribute names / noms d'attributs courants ──────────────
    /// <summary><b>EN/FR:</b> Temperature attribute. / Attribut température.</summary>
    public const string AttrTemperature = "temperature";
    /// <summary><b>EN/FR:</b> Humidity attribute. / Attribut humidité.</summary>
    public const string AttrHumidity = "humidity";
    /// <summary><b>EN/FR:</b> Barometric pressure. / Pression barométrique.</summary>
    public const string AttrBarometer = "barometer";
    /// <summary><b>EN/FR:</b> Battery state. / État batterie.</summary>
    public const string AttrBattery = "battery";
    /// <summary><b>EN/FR:</b> Signal strength. / Force du signal.</summary>
    public const string AttrSignal = "signal";
    /// <summary><b>EN/FR:</b> Motion ON/OFF. / Mouvement ON/OFF.</summary>
    public const string AttrMotion = "motion";
    /// <summary><b>EN/FR:</b> Tamper ON/OFF.</summary>
    public const string AttrTamper = "tamper";
    /// <summary><b>EN/FR:</b> Decoded status string. / Chaîne de statut décodée.</summary>
    public const string AttrStatus = "status";
    /// <summary><b>EN/FR:</b> ON/OFF state. / État ON/OFF.</summary>
    public const string AttrState = "state";
    /// <summary><b>EN/FR:</b> Decoded command name. / Nom de commande décodé.</summary>
    public const string AttrCommand = "command";
    /// <summary><b>EN/FR:</b> Dimmer level 0-100%. / Niveau dimmer 0-100%.</summary>
    public const string AttrLevel = "level";

    // ── EN/FR: availability payloads (zigbee2mqtt-compatible JSON format) ──────
    /// <summary><b>EN/FR:</b> Online JSON payload. / Charge utile JSON "online".</summary>
    public const string PayloadOnline = "{\"state\":\"online\"}";
    /// <summary><b>EN/FR:</b> Offline JSON payload. / Charge utile JSON "offline".</summary>
    public const string PayloadOffline = "{\"state\":\"offline\"}";
}
