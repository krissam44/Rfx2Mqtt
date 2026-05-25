using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> YAML implementation of <see cref="IDeviceRepository"/>.
/// <br/><br/>
/// Storage: <c>{ContentRoot}/data/devices.yaml</c>.
/// <br/><br/>
/// Automatic migration on first startup:
/// <ul>
///   <li>If <c>devices.yaml</c> does NOT exist but <c>appsettings.json</c> contains legacy
///       device sections, import the lists into the YAML and remove them from the JSON
///       (with a <c>.bak</c> backup) — so we never keep two sources of truth.</li>
///   <li>If <c>devices.yaml</c> EXISTS and <c>appsettings.json</c> still has device sections,
///       log a warning. The JSON values are ignored.</li>
/// </ul>
/// Thread-safety:
/// <ul>
///   <li>Reads (<see cref="Snapshot"/>): <see cref="Volatile.Read(ref DeviceCollection)"/> on the reference — no lock.</li>
///   <li>Writes (<see cref="SaveAsync"/>): <see cref="SemaphoreSlim"/> serializes disk I/O.</li>
/// </ul>
/// <br/>
/// <b>FR:</b> Implémentation YAML d'<see cref="IDeviceRepository"/>.
/// <br/><br/>
/// Stockage : <c>{ContentRoot}/data/devices.yaml</c>.
/// <br/><br/>
/// Migration automatique au premier démarrage :
/// <ul>
///   <li>Si <c>devices.yaml</c> n'existe pas mais qu'<c>appsettings.json</c> contient des sections
///       d'appareils legacy, importer les listes dans le YAML et les retirer du JSON (avec
///       sauvegarde <c>.bak</c>) — pour ne jamais garder deux sources de vérité.</li>
///   <li>Si <c>devices.yaml</c> EXISTE et qu'<c>appsettings.json</c> contient encore des sections,
///       logger un warning. Les valeurs du JSON sont ignorées.</li>
/// </ul>
/// Thread-safety :
/// <ul>
///   <li>Lecture (<see cref="Snapshot"/>) : <see cref="Volatile.Read(ref DeviceCollection)"/> sur la référence — pas de lock.</li>
///   <li>Écriture (<see cref="SaveAsync"/>) : <see cref="SemaphoreSlim"/> sérialise les I/O disque.</li>
/// </ul>
/// </summary>
public class YamlDeviceRepository : IDeviceRepository
{
    private const string DevicesFileName = "data/devices.yaml";
    private const string AppSettingsFileName = "appsettings.json";

    private readonly ILogger<YamlDeviceRepository> _logger;
    private readonly string _devicesPath;
    private readonly string _appSettingsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private DeviceCollection _snapshot = DeviceCollection.Empty;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <inheritdoc />
    public DeviceCollection Snapshot => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>
    /// <b>EN:</b> Constructor — initializes paths and runs the bootstrap/migration synchronously.<br/>
    /// <b>FR:</b> Constructeur — initialise les chemins et lance le bootstrap/migration en synchrone.
    /// </summary>
    public YamlDeviceRepository(
        ILogger<YamlDeviceRepository> logger,
        IWebHostEnvironment env,
        IOptions<RfxComOptions> legacyOptions)
    {
        _logger = logger;
        _devicesPath = Path.Combine(env.ContentRootPath, DevicesFileName);
        _appSettingsPath = Path.Combine(env.ContentRootPath, AppSettingsFileName);

        InitializeAsync(legacyOptions.Value).GetAwaiter().GetResult();
    }

    /// <summary>
    /// <b>EN:</b> Bootstrap: decide between "load existing YAML", "migrate legacy" or "create empty".<br/>
    /// <b>FR:</b> Bootstrap : choisit entre "charger le YAML existant", "migrer le legacy" ou "créer un vide".
    /// </summary>
    private async Task InitializeAsync(RfxComOptions legacyOptions)
    {
        var directory = Path.GetDirectoryName(_devicesPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(_devicesPath))
        {
            // EN: Normal path — YAML already exists.
            // FR: Cas normal — YAML déjà créé.
            _snapshot = LoadFromYaml();
            _logger.LogInformation(
                "Inventaire chargé depuis {Path} : {Oregon} Oregon, {Security} Security, {Somfy} Somfy, {Chacon} Chacon",
                _devicesPath, _snapshot.Oregon.Count, _snapshot.Security.Count, _snapshot.Somfy.Count, _snapshot.Chacon.Count);

            if (HasLegacyDevices(legacyOptions))
            {
                _logger.LogWarning(
                    "appsettings.json contient encore des sections d'appareils — elles sont IGNORÉES " +
                    "(source de vérité = {Path}). Pour nettoyer, supprime ces sections manuellement.",
                    _devicesPath);
            }
        }
        else if (HasLegacyDevices(legacyOptions))
        {
            // EN: Migration — appsettings → YAML.
            // FR: Migration — appsettings → YAML.
            _logger.LogInformation("Migration des appareils depuis appsettings.json vers {Path}", _devicesPath);
            _snapshot = new DeviceCollection
            {
                Oregon = legacyOptions.OregonDevices.ToList(),
                Security = legacyOptions.SecurityDevices.ToList(),
                Somfy = legacyOptions.SomfyDevices.ToList(),
                Chacon = legacyOptions.ChaconDevices.ToList(),
            };
            await WriteYamlAsync(_snapshot);
            try
            {
                CleanAppSettingsDeviceSections();
                _logger.LogInformation("Sections d'appareils retirées d'appsettings.json (sauvegarde .bak créée)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Migration YAML OK mais nettoyage d'appsettings.json échoué — les listes legacy " +
                    "resteront présentes (mais ignorées au runtime).");
            }
        }
        else
        {
            // EN: First start with no device anywhere — create an empty file to make manual editing easier.
            // FR: Premier démarrage sans aucun appareil — on crée un fichier vide pour faciliter l'édition manuelle.
            await WriteYamlAsync(DeviceCollection.Empty);
            _logger.LogInformation("Fichier d'inventaire vide créé : {Path}", _devicesPath);
        }
    }

    /// <summary>
    /// <b>EN:</b> Whether the legacy <c>RfxComOptions</c> still carries any device list.<br/>
    /// <b>FR:</b> Indique si les <c>RfxComOptions</c> legacy contiennent encore au moins un device.
    /// </summary>
    private static bool HasLegacyDevices(RfxComOptions opts) =>
        opts.OregonDevices.Count > 0
        || opts.SecurityDevices.Count > 0
        || opts.SomfyDevices.Count > 0
        || opts.ChaconDevices.Count > 0;

    /// <inheritdoc />
    public async Task SaveAsync(DeviceCollection devices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteYamlAsync(devices);
            Volatile.Write(ref _snapshot, devices);
            _logger.LogInformation(
                "Inventaire enregistré dans {Path} : {Oregon} Oregon, {Security} Security, {Somfy} Somfy, {Chacon} Chacon",
                _devicesPath, devices.Oregon.Count, devices.Security.Count, devices.Somfy.Count, devices.Chacon.Count);
        }
        finally
        {
            _writeLock.Release();
        }

        try { Changed?.Invoke(); }
        catch (Exception ex) { _logger.LogError(ex, "Erreur dans un handler Changed"); }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var fresh = File.Exists(_devicesPath) ? LoadFromYaml() : DeviceCollection.Empty;
            Volatile.Write(ref _snapshot, fresh);
            _logger.LogInformation("Inventaire rechargé depuis {Path}", _devicesPath);
        }
        finally
        {
            _writeLock.Release();
        }

        try { Changed?.Invoke(); }
        catch (Exception ex) { _logger.LogError(ex, "Erreur dans un handler Changed"); }
    }

    /// <summary>
    /// <b>EN:</b> Loads and parses the YAML file. Returns an empty collection on any error
    /// (parse failures are logged so the user can fix the YAML by hand).<br/>
    /// <b>FR:</b> Charge et parse le fichier YAML. Renvoie une collection vide en cas d'erreur
    /// (les erreurs de parsing sont loggées pour que l'utilisateur puisse corriger le YAML à la main).
    /// </summary>
    private DeviceCollection LoadFromYaml()
    {
        try
        {
            var yaml = File.ReadAllText(_devicesPath);
            if (string.IsNullOrWhiteSpace(yaml))
                return DeviceCollection.Empty;

            var raw = _yamlDeserializer.Deserialize<YamlRoot?>(yaml);
            if (raw is null)
                return DeviceCollection.Empty;

            return new DeviceCollection
            {
                Oregon = raw.Oregon ?? [],
                Security = raw.Security ?? [],
                Somfy = raw.Somfy ?? [],
                Chacon = raw.Chacon ?? [],
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erreur lors du chargement de {Path} — démarrage avec un inventaire vide. " +
                "Vérifie la syntaxe YAML manuellement.", _devicesPath);
            return DeviceCollection.Empty;
        }
    }

    /// <summary>
    /// <b>EN:</b> Serializes to YAML and atomically swaps the file (write to <c>.tmp</c>, then rename)
    /// to avoid leaving a corrupted file behind if the process crashes mid-write.<br/>
    /// <b>FR:</b> Sérialise en YAML et remplace le fichier de manière atomique (écrit en <c>.tmp</c>,
    /// puis renomme) pour éviter de laisser un fichier corrompu en cas de crash en cours d'écriture.
    /// </summary>
    private async Task WriteYamlAsync(DeviceCollection devices)
    {
        var root = new YamlRoot
        {
            Oregon = devices.Oregon.ToList(),
            Security = devices.Security.ToList(),
            Somfy = devices.Somfy.ToList(),
            Chacon = devices.Chacon.ToList(),
        };
        var header =
            "# Rfx2Mqtt device inventory / Inventaire des appareils Rfx2Mqtt.\n" +
            "# Edited automatically by the app (Appareils page) or manually.\n" +
            "# Modifié automatiquement par l'application (page Appareils) ou à la main.\n" +
            "\n";
        var yaml = header + _yamlSerializer.Serialize(root);

        var tmpPath = _devicesPath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, yaml);
        File.Move(tmpPath, _devicesPath, overwrite: true);
    }

    /// <summary>
    /// <b>EN:</b> Removes the device sections from <c>appsettings.json</c> after migration.
    /// Creates a <c>.bak</c> of the original file before overwriting.<br/>
    /// <b>FR:</b> Retire les sections d'appareils d'<c>appsettings.json</c> après migration.
    /// Crée une sauvegarde <c>.bak</c> du fichier original avant écrasement.
    /// </summary>
    private void CleanAppSettingsDeviceSections()
    {
        if (!File.Exists(_appSettingsPath)) return;

        var content = File.ReadAllText(_appSettingsPath);
        var root = JsonNode.Parse(content) as JsonObject;
        if (root is null) return;

        var rfxSection = root["RfxCom"] as JsonObject;
        if (rfxSection is null) return;

        bool changed = false;
        foreach (var key in new[] { "OregonDevices", "SecurityDevices", "SomfyDevices", "ChaconDevices" })
        {
            if (rfxSection.ContainsKey(key))
            {
                rfxSection.Remove(key);
                changed = true;
            }
        }

        if (!changed) return;

        var backupPath = _appSettingsPath + ".bak";
        File.Copy(_appSettingsPath, backupPath, overwrite: true);

        var newJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_appSettingsPath, newJson);
    }

    /// <summary>
    /// <b>EN:</b> POCO mirroring the YAML root — used by YamlDotNet for (de)serialization.<br/>
    /// <b>FR:</b> POCO miroir de la racine YAML — utilisé par YamlDotNet pour la (dé)sérialisation.
    /// </summary>
    private sealed class YamlRoot
    {
        public List<OregonDevice>? Oregon { get; set; }
        public List<SecurityDevice>? Security { get; set; }
        public List<SomfyDevice>? Somfy { get; set; }
        public List<ChaconDevice>? Chacon { get; set; }
    }
}
