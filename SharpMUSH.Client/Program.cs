using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;
using SharpMUSH.Client;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Slugify;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddMudServices();
builder.Services.AddLogging();
builder.Services.AddSingleton<ISlugHelper, SlugHelper>();
builder.Services.AddSingleton<WikiMarkdigPipeline>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<WikiAssetService>();
builder.Services.AddSingleton<CharacterDirectoryService>();
builder.Services.AddSingleton<SchemaAppService>();
builder.Services.AddSingleton<ApplicationRegistryClient>();
// Loads + resolves plugin-shipped compiled Blazor components at runtime (gate-guarded server-side; renders
// by reflection only — references zero plugin types). Caches loaded assemblies (no unload, by design).
builder.Services.AddSingleton<PluginComponentLoader>();
builder.Services.AddSingleton<RoleRegistryClient>();
builder.Services.AddSingleton<GalleryService>();
builder.Services.AddSingleton<MailService>();
// Scene data is served by the server API; the WASM client has no local ISceneService
// implementation — reads go through this HTTP service, writes go through a game command.
builder.Services.AddSingleton<SceneService>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<ConfigSchemaService>();
builder.Services.AddSingleton<RestrictionsService>();
builder.Services.AddSingleton<PackagesAdminService>();
builder.Services.AddSingleton<BannedNamesService>();
builder.Services.AddSingleton<SitelockService>();
builder.Services.AddSingleton<AdminAccountsService>();
// Registers the terminal facades — see AddTerminalServices for the rationale.
builder.Services.AddTerminalServices();
builder.Services.AddSingleton<MushQueryService>();
builder.Services.AddHttpClient("help", c =>
{
	c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddSingleton<HelpService>(sp =>
{
	var factory = sp.GetRequiredService<IHttpClientFactory>();
	return new HelpService(factory.CreateClient("help"));
});
builder.Services.AddScoped<CredentialService>();
builder.Services.AddSingleton<OttAuthService>();
builder.Services.AddSingleton<AccountAuthService>();
builder.Services.AddSingleton<IAccountAuthState>(sp => sp.GetRequiredService<AccountAuthService>());
builder.Services.AddSingleton<DatabaseConversionService>();
builder.Services.AddSingleton<IThemeService, ThemeService>();

var registry = new WidgetRegistry();
registry.Register(new QuickLinksWidgetDescriptor());
registry.Register(new WelcomeTextWidgetDescriptor());
registry.Register(new CharacterDirectoryWidgetDescriptor());
registry.Register(new CharacterGalleryWidgetDescriptor());
registry.Register(new WikiIndexWidgetDescriptor());
registry.Register(new WikiBodyWidgetDescriptor());
registry.Register(new SpacerWidgetDescriptor());
registry.Register(new StatsWidgetDescriptor());
registry.Register(new ActiveSceneWidgetDescriptor());
registry.Register(new RecentWikiActivityWidgetDescriptor());
registry.Register(new OnlineCharactersWidgetDescriptor());
registry.Register(new QuickstartWidgetDescriptor());
registry.Register(new SchemaWidgetDescriptor());

// Where the server's API lives: same origin as the app itself unless configuration says otherwise
// (the dev launch profile splits the client on http:8080 from the API on https:8081 and opts in via
// appsettings.Development.json). Resolved once here and shared by every API caller below, so the
// answer cannot drift between them.
var apiBaseAddress = ApiBaseAddressResolver.Resolve(
	builder.HostEnvironment.BaseAddress,
	builder.Configuration[ApiBaseAddressResolver.ConfigurationKey]);

// Bridge Widget-kind Dynamic Applications (Area 21) into the layout palette: load the registry once
// at startup (anonymous) and register a synthetic widget per app, rendered by SchemaWidget. The
// catalog is also injected so SchemaWidget can resolve a placement's schema/data routes by slug.
var applicationCatalog = await ApplicationCatalog.LoadAsync(apiBaseAddress);
foreach (var widgetApp in applicationCatalog.WidgetApps)
{
	registry.Register(new ApplicationPortalWidget(widgetApp));
}
builder.Services.AddSingleton(applicationCatalog);
builder.Services.AddSingleton<IWidgetRegistry>(registry);
builder.Services.AddSingleton<ILayoutService, LayoutService>();
builder.Services.AddSingleton<ICharacterStateService, CharacterStateService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IGameHubConnectionFactory>(sp =>
	new GameHubConnectionFactory(
		$"{builder.HostEnvironment.BaseAddress.TrimEnd('/')}/hubs/game",
		sp.GetRequiredService<IAccountAuthState>(),
		// Phase 9: scene realtime is a separate connection to the plugin-owned SceneHub at /hubs/scene.
		$"{builder.HostEnvironment.BaseAddress.TrimEnd('/')}/hubs/scene"));
builder.Services.AddSingleton<ConnectionStateService>();
builder.Services.AddSingleton<IConnectionStateService>(sp => sp.GetRequiredService<ConnectionStateService>());
// Same singleton, exposed for scene group join/leave (client-only control surface).
builder.Services.AddSingleton<ISceneHubControl>(sp => sp.GetRequiredService<ConnectionStateService>());

builder.Services.AddHttpClient("api", c => c.BaseAddress = apiBaseAddress);

if (builder.HostEnvironment.IsDevelopment())
{
	builder.Services.AddScoped<AuthenticationStateProvider, DebugAuthStateProvider>();
}
else
{
	builder.Services.AddScoped<AuthenticationStateProvider, AccountAuthStateProvider>();
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var app = builder.Build();

var jsRuntime = app.Services.GetRequiredService<IJSRuntime>();
var storedLocale = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", "locale");
CultureInfo culture;
try
{
	culture = new CultureInfo(storedLocale ?? "en");
}
catch (CultureNotFoundException)
{
	culture = new CultureInfo("en");
	await jsRuntime.InvokeVoidAsync("localStorage.removeItem", "locale");
}
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await app.RunAsync();