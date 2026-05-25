using Microsoft.Extensions.Options;

namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> Thread-safe, persistent state of the <c>PermitJoin</c> flag (a.k.a. discovery mode).
/// <br/><br/>
/// Why a dedicated service rather than mutating <c>IOptions&lt;RfxComOptions&gt;.Value</c>?
/// <ul>
///   <li><c>IOptions</c> is designed for immutable configuration.</li>
///   <li>Mutating from the MQTT thread while handlers read from the serial thread has no memory
///       visibility guarantee.</li>
///   <li>The state must survive restarts — otherwise the default from config is restored on every
///       deploy/power failure, which re-pollutes the whole broker with unknown devices.</li>
/// </ul>
/// Persistence goes through <see cref="UI.ConfigFileService"/> which writes <c>appsettings.json</c>.
/// Since <c>ConfigFileService</c> is <i>Scoped</i>, it's resolved via <see cref="IServiceScopeFactory"/>.
/// <br/><br/>
/// <b>FR:</b> État thread-safe et persistant du flag <c>PermitJoin</c> (mode découverte).
/// <br/><br/>
/// Pourquoi un service dédié plutôt que muter <c>IOptions&lt;RfxComOptions&gt;.Value</c> ?
/// <ul>
///   <li><c>IOptions</c> est conçu pour de la config immutable.</li>
///   <li>La mutation depuis le thread MQTT pendant que les handlers lisent depuis le thread série
///       n'a aucune garantie de visibilité mémoire.</li>
///   <li>L'état doit survivre aux redémarrages — sinon retour au défaut config à chaque
///       deploy/panne courant, ce qui repollue tout le broker avec des devices inconnus.</li>
/// </ul>
/// La persistance se fait via <see cref="UI.ConfigFileService"/> qui écrit dans
/// <c>appsettings.json</c>. Comme <c>ConfigFileService</c> est <i>Scoped</i>, on l'invoque via
/// <see cref="IServiceScopeFactory"/>.
/// </summary>
public class PermitJoinState
{
    private readonly ILogger<PermitJoinState> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// <b>EN:</b> Backing field — 0/1 to allow <see cref="Interlocked.Exchange(ref int, int)"/>.<br/>
    /// <b>FR:</b> Champ sous-jacent — 0/1 pour permettre <see cref="Interlocked.Exchange(ref int, int)"/>.
    /// </summary>
    private int _enabled;

    /// <summary>
    /// <b>EN:</b> Constructor — seeds the in-memory state from <see cref="RfxComOptions.PermitJoin"/>.<br/>
    /// <b>FR:</b> Constructeur — initialise l'état mémoire depuis <see cref="RfxComOptions.PermitJoin"/>.
    /// </summary>
    public PermitJoinState(
        ILogger<PermitJoinState> logger,
        IOptions<RfxComOptions> initialOptions,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _enabled = initialOptions.Value.PermitJoin ? 1 : 0;
    }

    /// <summary>
    /// <b>EN:</b> Current state — atomic read, safe from every thread.<br/>
    /// <b>FR:</b> État courant — lecture atomique, sûre depuis tous les threads.
    /// </summary>
    public bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    /// <summary>
    /// <b>EN:</b> Updates the flag and persists it to <c>appsettings.json</c>. Returns <c>true</c>
    /// if the value actually changed. If persistence fails, the in-memory state is still updated
    /// (logged as warning) so the running session keeps the new mode.<br/>
    /// <b>FR:</b> Met à jour le flag et le persiste dans <c>appsettings.json</c>. Retourne
    /// <c>true</c> si la valeur a réellement changé. En cas d'échec de persistance, l'état mémoire
    /// est tout de même mis à jour (warning loggé) pour que la session courante garde le nouveau
    /// mode.
    /// </summary>
    public async Task<bool> SetAsync(bool enable, CancellationToken cancellationToken = default)
    {
        var newValue = enable ? 1 : 0;
        var previous = Interlocked.Exchange(ref _enabled, newValue);
        if (previous == newValue)
            return false;

        try
        {
            await PersistAsync(enable, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PermitJoin → {State} : état mémoire OK mais persistance échouée " +
                "(la valeur sera perdue au prochain redémarrage)",
                enable ? "ACTIVÉ" : "DÉSACTIVÉ");
        }

        return true;
    }

    /// <summary>
    /// <b>EN:</b> Writes the new value to <c>appsettings.json</c> through a fresh DI scope (since
    /// <see cref="UI.ConfigFileService"/> is Scoped).<br/>
    /// <b>FR:</b> Écrit la nouvelle valeur dans <c>appsettings.json</c> via un scope DI frais (car
    /// <see cref="UI.ConfigFileService"/> est Scoped).
    /// </summary>
    private Task PersistAsync(bool enable, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var scope = _scopeFactory.CreateScope();
            var configFile = scope.ServiceProvider.GetRequiredService<UI.ConfigFileService>();
            var options = configFile.LoadRfxComOptions();
            if (options.PermitJoin == enable)
                return;
            options.PermitJoin = enable;
            configFile.SaveRfxComOptions(options);
            _logger.LogInformation("PermitJoin = {State} persisté dans appsettings.json",
                enable ? "true" : "false");
        }, cancellationToken);
    }
}
