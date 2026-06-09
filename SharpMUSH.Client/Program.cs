using System.Globalization;
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
builder.Services.AddSingleton<IWikiService, InMemoryWikiService>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<ISceneService, InMemorySceneService>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<ConfigSchemaService>();
builder.Services.AddSingleton<RestrictionsService>();
builder.Services.AddSingleton<BannedNamesService>();
builder.Services.AddSingleton<SitelockService>();
builder.Services.AddSingleton<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddSingleton<ITerminalService, TerminalService>();
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
builder.Services.AddSingleton<DatabaseConversionService>();
builder.Services.AddSingleton<IThemeService, ThemeService>();

// Widget system
var registry = new WidgetRegistry();
registry.Register(new QuickLinksWidgetDescriptor());
registry.Register(new WelcomeTextWidgetDescriptor());
builder.Services.AddSingleton<IWidgetRegistry>(registry);
builder.Services.AddSingleton<ILayoutService, LayoutService>();
builder.Services.AddSingleton<ICharacterStateService, CharacterStateService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IGameHubConnectionFactory>(_ =>
	new GameHubConnectionFactory(
		$"{builder.HostEnvironment.BaseAddress.TrimEnd('/')}/hubs/game"));
builder.Services.AddSingleton<IConnectionStateService, ConnectionStateService>();

builder.Services.AddHttpClient("api", sp =>
{
	var uri = new UriBuilder(builder.HostEnvironment.BaseAddress)
	{
		Scheme = "https",  // Use HTTPS for secure API calls
		Port = 8081        // HTTPS port
	};
	sp.BaseAddress = uri.Uri;
});

if (builder.HostEnvironment.IsDevelopment())
{
	builder.Services.AddScoped<AuthenticationStateProvider, DebugAuthStateProvider>();
}
else
{
	builder.Services.AddOidcAuthentication(options =>
	{
		// Configure your authentication provider options here.
		// For more information, see https://aka.ms/blazor-standalone-auth
		builder.Configuration.Bind("Local", options.ProviderOptions);
	});
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

var app = builder.Build();

// Seed the "home" wiki page so the landing page has default content on first load.
// InMemoryWikiService: CreateAsync is a no-op if the slug already exists, so this is safe to run every startup.
var wikiSeed = app.Services.GetRequiredService<IWikiService>();
await wikiSeed.CreateAsync(
    title: "Home",
    markdown: """
        # Welcome to SharpMUSH!

        This is your MUSH's home page. It's stored as a wiki article and can be edited
        by any authorised user.

        ## Getting started

        - Connect with a MU* client on port **4201**
        - Or use the terminal panel below
        - Create a character with `create <name> <password>`
        - Then log in with `connect <name> <password>`

        ## About SharpMUSH

        SharpMUSH is a modern, open-source MUSH server written in .NET, targeting
        PennMUSH compatibility. See the [wiki](/wiki/wiki-index) for more information.
        """,
    authorDbref: "#1");

// Restore saved locale from localStorage (defaults to "en" if not set)
var jsRuntime = app.Services.GetRequiredService<IJSRuntime>();
var storedLocale = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", "locale");
CultureInfo culture;
try
{
	culture = new CultureInfo(storedLocale ?? "en");
}
catch (CultureNotFoundException)
{
	// Invalid locale stored in localStorage — reset to English
	culture = new CultureInfo("en");
	await jsRuntime.InvokeVoidAsync("localStorage.removeItem", "locale");
}
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await app.RunAsync();