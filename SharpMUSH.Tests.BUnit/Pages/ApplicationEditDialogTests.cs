using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Pages.Admin.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Records the body of each POST to <c>api/applications</c> and answers OK.</summary>
internal sealed class ApplicationPostCapture : HttpMessageHandler
{
	public string? Posted { get; private set; }
	public PortalApplication? Configured { get; set; }
	public HttpStatusCode ConfigurationStatus { get; set; } = HttpStatusCode.OK;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/configuration", StringComparison.Ordinal))
			return new HttpResponseMessage(ConfigurationStatus)
			{
				Content = new StringContent(JsonSerializer.Serialize(Configured, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
			};

		if (request.Method == HttpMethod.Post && request.Content is not null)
		{
			Posted = await request.Content.ReadAsStringAsync(ct);
		}

		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
	}
}

/// <summary>
/// The admin Applications editor shows an application's layout scope and OOB package (README §7.4), and
/// saving an existing application sends back every field it did not edit — including the component
/// fields the dialog has no inputs for.
/// </summary>
public class ApplicationEditDialogTests : TrackingBunitContext
{
	private readonly ApplicationPostCapture _http = new();

	public ApplicationEditDialogTests()
	{
		var client = Track(new HttpClient(_http) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		Services.AddMudServices();
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static readonly PortalApplication Weather = new(
		"weather", "Weather", null, "Widget", "http/weather/schema", null, null, "Player", null,
		["RightSidebar"], 30, "weather-app", ApplicationRenderKind.Component,
		"api/plugins/weather/ui/Weather.Ui.dll", "Weather.Ui.Panel", Scope: "play", OobPackage: "weather.now");

	private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(PortalApplication? existing)
	{
		_http.Configured ??= existing;
		var provider = Render<MudDialogProvider>();
		var dialogs = Services.GetRequiredService<IDialogService>();
		var parameters = new DialogParameters<ApplicationEditDialog> { { x => x.Existing, existing } };
		await provider.InvokeAsync(() => dialogs.ShowAsync<ApplicationEditDialog>("edit", parameters));
		return provider;
	}

	[TUnit.Core.Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task SavePreservesPrivateTileForManualAndPackageApplications(bool hosted, bool packaged)
	{
		var tile = new ApplicationConfigurationTile("weather-settings", "Weather settings", "Private settings", "Settings",
			"/apps/weather/settings", "config.admin");
		using var document = JsonDocument.Parse("{\"channel\":\"private-channel\"}");
		var configured = Weather with
		{
			Kind = "Page",
			Zones = [],
			RenderKind = hosted ? ApplicationRenderKind.Hosted : ApplicationRenderKind.Schema,
			OwningPackage = packaged ? "weather-app" : null,
			DefinitionId = hosted ? "weather-definition" : null,
			Config = hosted ? document.RootElement.Clone() : null,
			ConfigurationTile = tile
		};
		_http.Configured = configured;
		var provider = await ShowAsync(configured with { Config = null, ConfigurationTile = null });
		await provider.FindAll("input").First(input => input.GetAttribute("value") == "Weather").ChangeAsync("Renamed weather");
		await provider.FindAll("button").First(button => button.TextContent.Contains("Save")).ClickAsync();
		provider.WaitForAssertion(() =>
		{
			if (_http.Posted is null) throw new InvalidOperationException("nothing posted yet");
		});
		var saved = JsonSerializer.Deserialize<PortalApplication>(_http.Posted!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		await Assert.That(saved.DisplayName).IsEqualTo("Renamed weather");
		await Assert.That(saved.ConfigurationTile).IsEqualTo(tile);
		await Assert.That(saved.OwningPackage).IsEqualTo(configured.OwningPackage);
		if (hosted) await Assert.That(saved.Config!.Value.GetProperty("channel").GetString()).IsEqualTo("private-channel");
	}

	[TUnit.Core.Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedPrivateConfigurationReadBlocksSaving(bool hosted)
	{
		_http.ConfigurationStatus = HttpStatusCode.ServiceUnavailable;
		var provider = await ShowAsync(Weather with { RenderKind = hosted ? ApplicationRenderKind.Hosted : ApplicationRenderKind.Schema });
		var save = provider.FindAll("button").First(button => button.TextContent.Contains("Save"));
		await Assert.That(save.HasAttribute("disabled")).IsTrue();
		await Assert.That(provider.Markup).Contains("Application configuration could not be read");
		await save.ClickAsync();
		await Assert.That(_http.Posted).IsNull();
	}

	[TUnit.Core.Test]
	public async Task ShowsScopeAndOobPackageFields_WithTheirValues()
	{
		var provider = await ShowAsync(Weather);

		await Assert.That(provider.Markup).Contains("LayAppScopeOptional");
		await Assert.That(provider.Markup).Contains("LayAppOobPackageOptional");
		var values = provider.FindAll("input").Select(i => i.GetAttribute("value")).ToList();
		await Assert.That(values).Contains("play");
		await Assert.That(values).Contains("weather.now");
	}

	[TUnit.Core.Test]
	public async Task Save_SendsScopeOobPackageAndTheUneditedComponentFields()
	{
		var provider = await ShowAsync(Weather);

		await provider.FindAll("button").First(b => b.TextContent.Contains("Save")).ClickAsync();
		provider.WaitForAssertion(() =>
		{
			if (_http.Posted is null)
				throw new InvalidOperationException("nothing posted yet");
		}, TimeSpan.FromSeconds(5));

		using var body = JsonDocument.Parse(_http.Posted!);
		var root = body.RootElement;
		await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("play");
		await Assert.That(root.GetProperty("oobPackage").GetString()).IsEqualTo("weather.now");
		await Assert.That(root.GetProperty("renderKind").GetString()).IsEqualTo(ApplicationRenderKind.Component);
		await Assert.That(root.GetProperty("componentAssemblyUrl").GetString()).IsEqualTo("api/plugins/weather/ui/Weather.Ui.dll");
		await Assert.That(root.GetProperty("componentTypeName").GetString()).IsEqualTo("Weather.Ui.Panel");
	}
}
