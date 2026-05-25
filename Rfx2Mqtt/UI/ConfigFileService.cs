using System.Text.Json;
using System.Text.Json.Nodes;
using Rfx2Mqtt.Configuration;

namespace Rfx2Mqtt.UI;

/// <summary>
/// <b>EN:</b> Reads and writes the bridge's <c>appsettings.json</c>. Used by the UI configuration
/// pages. Changes are applied to the file but require a service restart to take effect (unless
/// the consumer uses <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>).
/// <para>This service is registered as <i>Scoped</i> — do NOT inject it directly into singletons;
/// resolve via <see cref="IServiceScopeFactory"/> instead.</para>
/// <br/>
/// <b>FR:</b> Lit et écrit le fichier <c>appsettings.json</c> du bridge. Utilisé par les pages
/// de configuration de l'UI. Les modifications sont appliquées au fichier mais nécessitent un
/// redémarrage du service pour être effectives (sauf si le consommateur utilise
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>).
/// <para>Ce service est enregistré en <i>Scoped</i> — NE PAS l'injecter directement dans des
/// singletons ; passer par <see cref="IServiceScopeFactory"/>.</para>
/// </summary>
public class ConfigFileService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ConfigFileService> _logger;

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// <b>EN:</b> Constructor.<br/>
    /// <b>FR:</b> Constructeur.
    /// </summary>
    public ConfigFileService(IWebHostEnvironment env, ILogger<ConfigFileService> logger)
    {
        _env = env;
        _logger = logger;
    }

    private string AppSettingsPath =>
        Path.Combine(_env.ContentRootPath, "appsettings.json");

    // ── EN/FR: Read / Lecture ──────────────────────────────────────────────────

    /// <summary>
    /// <b>EN:</b> Loads the <c>RfxCom</c> section. Returns defaults if absent.<br/>
    /// <b>FR:</b> Charge la section <c>RfxCom</c>. Renvoie les défauts si absente.
    /// </summary>
    public RfxComOptions LoadRfxComOptions()
    {
        var node = ReadRoot();
        var section = node["RfxCom"];
        if (section is null) return new RfxComOptions();
        return section.Deserialize<RfxComOptions>() ?? new RfxComOptions();
    }

    /// <summary>
    /// <b>EN:</b> Loads the <c>Mqtt</c> section. Returns defaults if absent.<br/>
    /// <b>FR:</b> Charge la section <c>Mqtt</c>. Renvoie les défauts si absente.
    /// </summary>
    public MqttOptions LoadMqttOptions()
    {
        var node = ReadRoot();
        var section = node["Mqtt"];
        if (section is null) return new MqttOptions();
        return section.Deserialize<MqttOptions>() ?? new MqttOptions();
    }

    // ── EN/FR: Write / Écriture ────────────────────────────────────────────────

    /// <summary>
    /// <b>EN:</b> Patches the <c>RfxCom</c> section into <c>appsettings.json</c>.<br/>
    /// <b>FR:</b> Met à jour la section <c>RfxCom</c> dans <c>appsettings.json</c>.
    /// </summary>
    public void SaveRfxComOptions(RfxComOptions options)
    {
        PatchSection("RfxCom", options);
        _logger.LogInformation("appsettings.json mis à jour (section RfxCom)");
    }

    /// <summary>
    /// <b>EN:</b> Patches the <c>Mqtt</c> section into <c>appsettings.json</c>.<br/>
    /// <b>FR:</b> Met à jour la section <c>Mqtt</c> dans <c>appsettings.json</c>.
    /// </summary>
    public void SaveMqttOptions(MqttOptions options)
    {
        PatchSection("Mqtt", options);
        _logger.LogInformation("appsettings.json mis à jour (section Mqtt)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private JsonNode ReadRoot()
    {
        var json = File.ReadAllText(AppSettingsPath);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    private void PatchSection<T>(string sectionName, T value)
    {
        var root = ReadRoot() as JsonObject ?? new JsonObject();
        var serialized = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null   // keep PascalCase as in the original file / garde le PascalCase original
        });
        root[sectionName] = serialized;

        File.WriteAllText(AppSettingsPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
