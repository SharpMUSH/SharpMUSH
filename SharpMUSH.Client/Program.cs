using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SharpMUSH.Client;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using Slugify;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddLogging();
builder.Services.AddSingleton<ISlugHelper, SlugHelper>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<DatabaseConversionService>();

builder.Services.AddHttpClient("api", sp =>
{
	var uri = new UriBuilder(builder.HostEnvironment.BaseAddress)
	{
		Port = 7296
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
	
await app.RunAsync();