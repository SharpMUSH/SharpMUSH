using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SharpMUSH.Portal;
using SharpMUSH.Portal.Authentication;
using SharpMUSH.Portal.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddMudServices();
builder.Services.AddSingleton<Slugify.ISlugHelper, Slugify.SlugHelper>();
builder.Services.AddSingleton<WikiService>();
builder.Services.AddSingleton<AdminConfigService>();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

if (builder.HostEnvironment.IsDevelopment())
{
	builder.Services.AddScoped<AuthenticationStateProvider, DebugAuthStateProvicer>();
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

await builder.Build().RunAsync();