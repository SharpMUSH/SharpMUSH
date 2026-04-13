using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;
using SharpMUSH.Client;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using Slugify;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddMudServices();
builder.Services.AddLogging();
builder.Services.AddSingleton<ISlugHelper, SlugHelper>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<ConfigSchemaService>();
builder.Services.AddSingleton<RestrictionsService>();
builder.Services.AddSingleton<BannedNamesService>();
builder.Services.AddSingleton<SitelockService>();
builder.Services.AddSingleton<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddSingleton<DatabaseConversionService>();

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
	culture = new CultureInfo("en");
}
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await app.RunAsync();