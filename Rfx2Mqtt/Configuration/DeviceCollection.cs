namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> Immutable snapshot of every device managed by the bridge. A new instance is created
/// for every edit (records + <c>with</c> expressions). Stored on disk in <c>data/devices.yaml</c>,
/// kept separate from <c>appsettings.json</c> which contains only technical settings (broker, port,
/// protocols).
/// <br/><br/>
/// <i>Why split appsettings vs. devices?</i>
/// <ul>
///   <li><b>appsettings.json</b> = application configuration (rarely modified, by a developer)</li>
///   <li><b>devices.yaml</b>    = business inventory (often modified via UI or SSH)</li>
/// </ul>
/// Mixing them blurs the boundary, bloats Git diffs and complicates deployments.
/// <br/><br/>
/// <b>FR:</b> Inventaire immuable de tous les appareils gérés par le bridge. Une nouvelle instance
/// est créée à chaque modification (records + expressions <c>with</c>). Stocké sur disque dans
/// <c>data/devices.yaml</c>, séparé d'<c>appsettings.json</c> qui ne contient que les paramètres
/// techniques (broker, port, protocoles).
/// <br/><br/>
/// <i>Pourquoi séparer appsettings et devices ?</i>
/// <ul>
///   <li><b>appsettings.json</b> = configuration de l'application (modifiée rarement, par un dev)</li>
///   <li><b>devices.yaml</b>    = inventaire métier (modifié souvent via l'UI ou en SSH)</li>
/// </ul>
/// Les mélanger brouille la frontière, alourdit les diffs Git et complique les déploiements.
/// </summary>
public record DeviceCollection
{
    /// <summary>
    /// <b>EN:</b> Oregon / Bresser / Cresta temperature-humidity probes.<br/>
    /// <b>FR:</b> Sondes T°/Humidité Oregon / Bresser / Cresta.
    /// </summary>
    public IReadOnlyList<OregonDevice> Oregon { get; init; } = [];

    /// <summary>
    /// <b>EN:</b> X10 Security sensors (motion detectors, door/window contacts).<br/>
    /// <b>FR:</b> Capteurs Security X10 (mouvement, contacts porte/fenêtre).
    /// </summary>
    public IReadOnlyList<SecurityDevice> Security { get; init; } = [];

    /// <summary>
    /// <b>EN:</b> Somfy RTS blinds (RX events + TX commands).<br/>
    /// <b>FR:</b> Volets Somfy RTS (événements RX + commandes TX).
    /// </summary>
    public IReadOnlyList<SomfyDevice> Somfy { get; init; } = [];

    /// <summary>
    /// <b>EN:</b> Chacon / DIO / HomeEasy EU lighting devices.<br/>
    /// <b>FR:</b> Équipements lumineux Chacon / DIO / HomeEasy EU.
    /// </summary>
    public IReadOnlyList<ChaconDevice> Chacon { get; init; } = [];

    /// <summary>
    /// <b>EN:</b> Empty snapshot — initial value before any load.<br/>
    /// <b>FR:</b> Snapshot vide — valeur initiale avant tout chargement.
    /// </summary>
    public static DeviceCollection Empty { get; } = new();

    /// <summary>
    /// <b>EN:</b> <c>true</c> if every list is empty.<br/>
    /// <b>FR:</b> <c>true</c> si toutes les listes sont vides.
    /// </summary>
    public bool IsEmpty =>
        Oregon.Count == 0 && Security.Count == 0 && Somfy.Count == 0 && Chacon.Count == 0;
}
