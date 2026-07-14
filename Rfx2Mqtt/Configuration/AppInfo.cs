using System.Reflection;

namespace Rfx2Mqtt.Configuration;

/// <summary>
/// <b>EN:</b> Static application identity: version, build date and copyright, read once from the
/// assembly attributes stamped at compile time (see the csproj: <c>Version</c>, <c>Copyright</c>
/// and the <c>BuildDate</c> AssemblyMetadata). Displayed in the MudBlazor UI footer, published on
/// the <c>{prefix}/info</c> MQTT topic and returned by the <c>/healthz</c> endpoint.<br/>
/// <b>FR:</b> Identité statique de l'application : version, date de build et copyright, lus une
/// seule fois depuis les attributs d'assembly figés à la compilation (voir le csproj :
/// <c>Version</c>, <c>Copyright</c> et l'AssemblyMetadata <c>BuildDate</c>). Affichés dans le
/// pied de page de l'UI MudBlazor, publiés sur le topic MQTT <c>{prefix}/info</c> et renvoyés
/// par l'endpoint <c>/healthz</c>.
/// </summary>
public static class AppInfo
{
    /// <summary><b>EN/FR:</b> Application name. / Nom de l'application.</summary>
    public const string Name = "Rfx2Mqtt";

    /// <summary><b>EN:</b> Semantic version (e.g. <c>"1.1.0"</c>). <b>FR:</b> Version sémantique.</summary>
    public static string Version { get; }

    /// <summary>
    /// <b>EN:</b> Build date <c>yyyy-MM-dd</c> (UTC), stamped at compile time.<br/>
    /// <b>FR:</b> Date de build <c>yyyy-MM-dd</c> (UTC), figée à la compilation.
    /// </summary>
    public static string ReleaseDate { get; }

    /// <summary><b>EN/FR:</b> Copyright notice. / Mention de copyright.</summary>
    public static string Copyright { get; }

    static AppInfo()
    {
        var assembly = typeof(AppInfo).Assembly;

        // InformationalVersion porte la version du csproj sans le suffixe ".0" de révision
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Retirer le suffixe "+hash" éventuel (SourceLink)
        var plusIndex = informational?.IndexOf('+') ?? -1;
        Version = plusIndex > 0 ? informational![..plusIndex]
                : informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        ReleaseDate = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "";

        Copyright = assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? $"© {DateTime.UtcNow.Year} Chriss-Consulting";
    }
}
