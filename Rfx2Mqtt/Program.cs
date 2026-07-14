// ─────────────────────────────────────────────────────────────────────────────
// EN: Rfx2Mqtt — entry point.
//     Wires up DI, configuration, Blazor Server hosting and the background Worker.
//     Reads two config sections: "RfxCom" (technical settings) and "Mqtt" (broker).
//     Device inventory is loaded by IDeviceRepository from data/devices.yaml.
//
// FR: Rfx2Mqtt — point d'entrée.
//     Branche DI, configuration, hébergement Blazor Server et le BackgroundService Worker.
//     Lit deux sections de config : "RfxCom" (paramètres techniques) et "Mqtt" (broker).
//     L'inventaire des appareils est chargé par IDeviceRepository depuis data/devices.yaml.
// ─────────────────────────────────────────────────────────────────────────────
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Rfx2Mqtt;
using Rfx2Mqtt.Configuration;
using Rfx2Mqtt.Devices.Handlers;
using Rfx2Mqtt.Discovery;
using Rfx2Mqtt.Health;
using Rfx2Mqtt.Mqtt;
using Rfx2Mqtt.Serial;
using Rfx2Mqtt.UI;

var builder = WebApplication.CreateBuilder(args);

// ── EN: Configuration / FR: Configuration ────────────────────────────────────
builder.Services.Configure<RfxComOptions>(builder.Configuration.GetSection(RfxComOptions.SectionName));
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));

// ── EN: Helpers / state — FR: Helpers / état ─────────────────────────────────
// EN: MqttTopics — topic-building helper, depends on the prefix from MqttOptions.
// FR: MqttTopics — helper de construction de topics, dépend du prefix dans MqttOptions.
builder.Services.AddSingleton<MqttTopics>(sp =>
    new MqttTopics(sp.GetRequiredService<IOptions<MqttOptions>>().Value.TopicPrefix));

// EN: PermitJoinState — runtime persistent state (toggled via MQTT, saved to appsettings.json).
// FR: PermitJoinState — état runtime persistant (toggle via MQTT, sauvé dans appsettings.json).
builder.Services.AddSingleton<PermitJoinState>();

// EN: IDeviceRepository — device inventory, stored in data/devices.yaml (separate from
//     appsettings.json — see DeviceCollection for the rationale).
// FR: IDeviceRepository — inventaire des appareils, stocké dans data/devices.yaml (séparé
//     d'appsettings.json — voir DeviceCollection pour la rationale).
builder.Services.AddSingleton<IDeviceRepository, YamlDeviceRepository>();

// ── EN/FR: RFXCom bridge services / Services bridge RFXCom ───────────────────
builder.Services.AddSingleton<RfxComProtocol>();
builder.Services.AddSingleton<RfxComSerialService>();
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<HomeAssistantDiscoveryService>();
builder.Services.AddSingleton<MatterBridgePublisher>();
builder.Services.AddSingleton<AvailabilityService>();

// EN: Packet handlers — registered as concrete (for direct injection in Worker for command
//     routing) AND as IPacketHandler (for the dispatch loop).
// FR: Handlers de paquets — enregistrés en concret (injection directe dans le Worker pour
//     le command routing) ET en IPacketHandler (pour la boucle de dispatch).
builder.Services.AddSingleton<TempHumidityHandler>();
builder.Services.AddSingleton<SomfyRtsHandler>();
builder.Services.AddSingleton<SecurityHandler>();
builder.Services.AddSingleton<Lighting2Handler>();
builder.Services.AddSingleton<IPacketHandler>(sp => sp.GetRequiredService<TempHumidityHandler>());
builder.Services.AddSingleton<IPacketHandler>(sp => sp.GetRequiredService<SomfyRtsHandler>());
builder.Services.AddSingleton<IPacketHandler>(sp => sp.GetRequiredService<SecurityHandler>());
builder.Services.AddSingleton<IPacketHandler>(sp => sp.GetRequiredService<Lighting2Handler>());

// EN: Main Worker Service. / FR: Worker Service principal.
builder.Services.AddHostedService<Worker>();

// ── EN/FR: UI services / Services UI ─────────────────────────────────────────
builder.Services.AddSingleton<UiEventService>();
builder.Services.AddScoped<ConfigFileService>();

// ── EN/FR: Blazor Server + MudBlazor ─────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// EN: /healthz — MQTT + serial + last-frame age (see BridgeHealthCheck).
// FR: /healthz — MQTT + série + âge de la dernière trame (voir BridgeHealthCheck).
builder.Services.AddHealthChecks()
    .AddCheck<BridgeHealthCheck>("bridge");

// ── EN/FR: Logging ───────────────────────────────────────────────────────────
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// EN: Web port — configurable via appsettings or env var.
// FR: Port web — configurable via appsettings ou variable d'environnement.
builder.WebHost.UseUrls(
    builder.Configuration["WebUi:Url"] ?? "http://0.0.0.0:5080");

// EN: Static Web Assets (_content/ files from RCL packages like MudBlazor) — only needed in
//     development (dotnet run, Visual Studio). In published / Docker mode the assets are
//     already physically copied into wwwroot/, and calling this can break /_framework/blazor.web.js.
// FR: Static Web Assets (fichiers _content/ des packages RCL comme MudBlazor) — utile uniquement
//     en développement (dotnet run, Visual Studio). En mode publié / Docker les fichiers sont
//     déjà physiquement dans wwwroot/, et appeler ceci peut casser /_framework/blazor.web.js.
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseStaticWebAssets();
}

var app = builder.Build();

// EN: MapStaticAssets() (NET 9+) serves both wwwroot/ files AND framework assets like
//     _framework/blazor.web.js from the staticwebassets endpoints manifest. Replaces the
//     legacy UseStaticFiles() which only serves physical files from wwwroot/.
// FR: MapStaticAssets() (NET 9+) sert à la fois les fichiers de wwwroot/ ET les assets
//     framework comme _framework/blazor.web.js depuis le manifest staticwebassets endpoints.
//     Remplace l'ancien UseStaticFiles() qui ne servait que les fichiers physiques.
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<Rfx2Mqtt.UI.Components.App>()
   .AddInteractiveServerRenderMode();

// EN: JSON health endpoint — HTTP 200 (Healthy/Degraded) or 503 (Unhealthy) with details.
// FR: Endpoint de santé JSON — HTTP 200 (Healthy/Degraded) ou 503 (Unhealthy) avec détails.
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            name = AppInfo.Name,
            version = AppInfo.Version,
            releaseDate = AppInfo.ReleaseDate,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    data = e.Value.Data
                })
        });
    }
});

app.Run();
