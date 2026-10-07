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

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
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
		var provider = Render<MudDialogProvider>();
		var dialogs = Services.GetRequiredService<IDialogService>();
		var parameters = new DialogParameters<ApplicationEditDialog> { { x => x.Existing, existing } };
		await provider.InvokeAsync(() => dialogs.ShowAsync<ApplicationEditDialog>("edit", parameters));
		return provider;
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
