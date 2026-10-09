using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;
using SharpMUSH.Client;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

// The markup layers this app can render: MarkupText resolves emitters and codecs through
// MarkupRegistry.Default, which throws until something sets it.
if (!MarkupRegistry.IsConfigured)
{
	// WithHtml(policy): a tag rendered here lands in a browser, so every one is held to the portal's
	// policy as it is written — including markup built before it reached us.
	// WithLayoutImages: a figure()'s picture is shown only when the game's image_hosts allows its host.
	MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml(TagwrapPolicy.Portal)
		.WithLayoutImages(PortalImagePolicy.Allows);
}

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSharedResourceLocalization();
builder.Services.AddMudServices();
builder.Services.AddLogging();
builder.Services.AddSingleton<WikiMarkdigPipeline>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<WikiAssetService>();
builder.Services.AddSingleton<CharacterDirectoryService>();
builder.Services.AddSingleton<ICharacterPictures>(sp => sp.GetRequiredService<CharacterDirectoryService>());
builder.Services.AddScoped<CharacterProfileService>();
builder.Services.AddScoped<SidebarCollapseService>();
builder.Services.AddSingleton<SchemaAppService>();
builder.Services.AddSingleton<ApplicationRegistryClient>();
// Loads + resolves plugin-shipped compiled Blazor components at runtime (gate-guarded server-side; renders
// by reflection only — references zero plugin types). Caches loaded assemblies (no unload, by design).
builder.Services.AddSingleton<PluginComponentLoader>();
builder.Services.AddSingleton<RoleRegistryClient>();
builder.Services.AddSingleton<GalleryService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<ICommHistory, CommHistoryService>();
// Scene data is served by the server API; the WASM client has no local ISceneService
// implementation — reads go through this HTTP service, writes go through a game command.
builder.Services.AddSingleton<SceneService>();
// Commands the portal issues itself, run as the acting character (POST api/commands).
builder.Services.AddSingleton<GameCommandService>();
// The pose types (api/scenes/types), read once per acting character; hiding one runs +scene/hide.
builder.Services.AddSingleton<PoseTypeService>();
builder.Services.AddSingleton<ChannelBrowserService>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<ConfigSchemaService>();
builder.Services.AddSingleton<RestrictionsService>();
builder.Services.AddSingleton<ServerInfoService>();
builder.Services.AddSingleton<SetupWizardService>();
builder.Services.AddSingleton<PackagesAdminService>();
builder.Services.AddSingleton<BannedNamesService>();
builder.Services.AddSingleton<MsspService>();
builder.Services.AddSingleton<SitelockService>();
builder.Services.AddSingleton<AdminAccountsService>();
builder.Services.AddSingleton<AdminGuestsService>();
builder.Services.AddSingleton<GameMessagesService>();
builder.Services.AddSingleton<PortalThemesAdminService>();
builder.Services.AddSingleton<AdminCharactersService>();
builder.Services.AddSingleton<AdminAuditService>();
builder.Services.AddSingleton<AdminBansService>();
builder.Services.AddSingleton<AdminServerService>();
// Registers the terminal facades — see AddTerminalServices for the rationale.
builder.Services.AddTerminalServices();
builder.Services.AddSingleton<MushQueryService>();
builder.Services.AddSingleton<ObjectApiService>();
builder.Services.AddHttpClient("help", c =>
{
	c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddSingleton<HelpService>(sp =>
{
	var factory = sp.GetRequiredService<IHttpClientFactory>();
	return new HelpService(factory.CreateClient("help"));
});
// The server's shipped helpfile corpus (/api/help). Distinct from HelpService above, which indexes
// mush-defs.json for the softcode editor's function drawer.
builder.Services.AddSingleton<GameHelpService>();
// The terminals register their own teardown rather than being reached into from the auth service:
// logging out of the portal must end the game-side session, but that is the terminal layer's
// business, and it sits above authentication, not below it.
builder.Services.AddSingleton<IAccountSessionEndingHandler, TerminalSessionTeardown>();
builder.Services.AddSingleton<AccountAuthService>();
builder.Services.AddSingleton<IAccountAuthState>(sp => sp.GetRequiredService<AccountAuthService>());
builder.Services.AddSingleton<PasskeyInterop>();
builder.Services.AddSingleton<AccountPasskeyService>();
builder.Services.AddSingleton<DatabaseConversionService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());

var registry = new WidgetRegistry();
foreach (var widget in BuiltInWidgets.All)
{
	registry.Register(widget);
}

// Where the server's API lives: same origin as the app itself unless configuration says otherwise
// (the dev launch profile splits the client on http:8080 from the API on https:8081 and opts in via
// appsettings.Development.json). Resolved once here and shared by every API caller below, so the
// answer cannot drift between them.
var apiBaseAddress = ApiBaseAddressResolver.Resolve(
	builder.HostEnvironment.BaseAddress,
	builder.Configuration[ApiBaseAddressResolver.ConfigurationKey]);
// Server file paths (/api/wiki-assets/...) in an <img> resolve against the API, not the page.
SharpMUSH.Client.Components.Kit.ApiUrl.UseBase(apiBaseAddress);

// Bridge Widget-kind Dynamic Applications (Area 21) into the layout palette: load the registry once
// per page load (anonymous) and register a synthetic widget per app, rendered by SchemaWidget. The
// catalog is also injected so SchemaWidget can resolve a placement's schema/data routes by slug.
// Started, not awaited: it loads while the runtime renders, rather than holding up the first frame
// for a round trip. See ApplicationCatalog's remarks for why nothing needs it sooner.
var applicationCatalog = ApplicationCatalog.StartLoading(apiBaseAddress, catalog =>
{
	foreach (var widgetApp in catalog.WidgetApps)
	{
		registry.Register(new ApplicationPortalWidget(widgetApp));
	}
});
builder.Services.AddSingleton(applicationCatalog);
builder.Services.AddSingleton<IWidgetRegistry>(registry);
builder.Services.AddSingleton<ILayoutService, LayoutService>();
builder.Services.AddSingleton<ICharacterStateService, CharacterStateService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
// The hubs live on the API, like every other server call: against the page's own address they reach only
// the standalone client dev host (or a separately hosted portal), which answers the negotiate with a 405.
builder.Services.AddSingleton<IGameHubConnectionFactory>(sp =>
	new GameHubConnectionFactory(
		new Uri(apiBaseAddress, "hubs/game").ToString(),
		sp.GetRequiredService<IAccountAuthState>(),
		// Phase 9: scene realtime is a separate connection to the plugin-owned SceneHub at /hubs/scene.
		new Uri(apiBaseAddress, "hubs/scene").ToString()));
// The commands this browser has sent, for Up and Down in the terminal and the composer.
builder.Services.AddSingleton<CommandHistory>();
builder.Services.AddSingleton<ScreenReaderMode>();
builder.Services.AddSingleton<ConnectionStateService>();
builder.Services.AddSingleton<IConnectionStateService>(sp => sp.GetRequiredService<ConnectionStateService>());
// Same singleton, exposed for scene group join/leave (client-only control surface).
builder.Services.AddSingleton<ISceneHubControl>(sp => sp.GetRequiredService<ConnectionStateService>());

builder.Services.AddTransient<AccountSessionBearerHandler>();
builder.Services.AddHttpClient("api", c => c.BaseAddress = apiBaseAddress)
	.AddHttpMessageHandler<AccountSessionBearerHandler>();

if (builder.HostEnvironment.IsDevelopment())
{
	builder.Services.AddScoped<AuthenticationStateProvider, DebugAuthStateProvider>();
}
else
{
	builder.Services.AddScoped<AuthenticationStateProvider, AccountAuthStateProvider>();
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore(options => options.AddPolicy(BuildNavCatalog.OverviewPolicy, policy => policy
	.RequireAuthenticatedUser()
	.RequireAssertion(context => BuildNavCatalog.MayOpenOverview(context.User))));
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var app = builder.Build();
app.Services.AttachPlayTerminalFeeds();

var jsRuntime = app.Services.GetRequiredService<IJSRuntime>();
var storedLocale = await jsRuntime.GetItemAsync(BrowserStore.Local, "locale");
CultureInfo culture;
try
{
	culture = new CultureInfo(storedLocale ?? "en");
}
catch (CultureNotFoundException)
{
	culture = new CultureInfo("en");
	await jsRuntime.RemoveItemAsync(BrowserStore.Local, "locale");
}
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Restore this tab's account session now, while the runtime renders, rather than when the first
// authenticated request asks for it. InitAsync caches its task, so every later caller awaits this
// same run. (No readiness check here: the server hands out the portal only once the game is ready —
// see PortalStartupPage in SharpMUSH.Server.)
_ = app.Services.GetRequiredService<AccountAuthService>().InitAsync();

// The last theme applied in this browser, read before the first render so ThemeProvider renders it once
// instead of rendering the default and then re-rendering the whole tree in the stored one. The game's
// themes and the character's own accent follow in the background.
await app.Services.GetRequiredService<ThemeService>().InitializeAsync();

// index.html is served with a static lang="en", so without this the document keeps
// claiming English whichever locale was picked: custom.css selects the CJK mono stack on
// :lang(zh), and a screen reader chooses its voice from the same attribute. The picker
// reloads the page on switch, so stamping it once at startup is enough. Failing to stamp it
// leaves the static "en", which is no reason to keep the portal from starting.
try
{
	await jsRuntime.InvokeVoidAsync("document.documentElement.setAttribute", "lang", culture.Name);
}
catch (JSException ex)
{
	app.Services.GetRequiredService<ILogger<Program>>().LogWarning(ex, "Could not set the document language to {Culture}", culture.Name);
}

await app.RunAsync();