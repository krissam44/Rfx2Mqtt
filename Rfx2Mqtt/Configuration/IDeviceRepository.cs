namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> Source of truth for the device list.
/// <br/><br/>
/// Usage pattern:
/// <ul>
///   <li><b>Reads</b> (handlers, UI): access <see cref="Snapshot"/> directly — no lock, atomic
///       reference swap on every write.</li>
///   <li><b>Writes</b> (UI): call <see cref="SaveAsync"/> with a fresh complete collection.</li>
/// </ul>
/// The <see cref="Changed"/> event is raised after every successful save — useful for consumers
/// that want to invalidate a cache. The current packet handlers read <see cref="Snapshot"/> on
/// every frame, so they don't need to subscribe.
/// <br/><br/>
/// <b>FR:</b> Source de vérité pour la liste des appareils.
/// <br/><br/>
/// Pattern d'utilisation :
/// <ul>
///   <li><b>Lecture</b> (handlers, UI) : accès direct à <see cref="Snapshot"/> — pas de lock,
///       remplacement atomique de la référence à chaque écriture.</li>
///   <li><b>Écriture</b> (UI) : appeler <see cref="SaveAsync"/> avec une nouvelle collection complète.</li>
/// </ul>
/// L'événement <see cref="Changed"/> est levé après chaque save réussi — utile pour les consommateurs
/// qui veulent invalider un cache. Les handlers actuels lisent <see cref="Snapshot"/> à chaque
/// trame donc n'ont pas besoin de s'abonner.
/// </summary>
public interface IDeviceRepository
{
    /// <summary>
    /// <b>EN:</b> Current snapshot — always non-null. Lock-free read.<br/>
    /// <b>FR:</b> Snapshot courant — toujours non-null. Lecture sans lock.
    /// </summary>
    DeviceCollection Snapshot { get; }

    /// <summary>
    /// <b>EN:</b> Replaces the full collection and persists it to disk.<br/>
    /// <b>FR:</b> Remplace la collection complète et la persiste sur disque.
    /// </summary>
    Task SaveAsync(DeviceCollection devices, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>EN:</b> Reloads from storage. Useful after an external edit of the YAML file.<br/>
    /// <b>FR:</b> Recharge depuis le stockage. Utile après édition externe du YAML.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>EN:</b> Raised after every successful <see cref="SaveAsync"/> or <see cref="ReloadAsync"/>.<br/>
    /// <b>FR:</b> Levé après chaque <see cref="SaveAsync"/> ou <see cref="ReloadAsync"/> réussi.
    /// </summary>
    event Action? Changed;
}
