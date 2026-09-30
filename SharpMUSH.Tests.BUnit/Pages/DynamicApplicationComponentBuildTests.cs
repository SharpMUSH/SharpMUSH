using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Serves one Component-kind application and 404s its assembly, recording every request.</summary>
file sealed class ComponentAppHandler : HttpMessageHandler
{
	public ConcurrentQueue<string> Requests { get; } = new();

	private const string AppDto = """
	{"slug":"hello-ui","displayName":"Hello UI","icon":"Badge","kind":"Page",
	 "schemaUrl":"","dataUrl":null,"submitRoute":null,"minimumRole":"Guest","navPlacement":null,
	 "zones":[],"order":0,"owningPackage":"hello","renderKind":"Component",
	 "componentAssemblyUrl":"api/plugins/hello/ui/Hello.dll","componentTypeName":"Hello.Widget"}
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		Requests.Enqueue(request.RequestUri!.AbsolutePath);
		return Task.FromResult(request.RequestUri.AbsolutePath == "/api/applications/hello-ui"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AppDto, Encoding.UTF8, "application/json") }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// A compiled-component application on a portal built without <c>PluginComponentSupport</c> (the stock
/// build: the UI surface such components compile against is trimmed) is refused with a notice naming the
/// build, not the generic "could not be loaded" that points an operator at allow_browser_code.
/// </summary>
public class DynamicApplicationComponentBuildTests : TrackingBunitContext
{
	private ConcurrentQueue<string> Seed(bool buildSupportsComponents)
	{
		var handler = new ComponentAppHandler();
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([]))
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new PluginComponentLoader(sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<PluginComponentLoader>.Instance, buildSupportsComponents))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddSingleton(new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		AddAuthorization();
		return handler.Requests;
	}

	private IRenderedComponent<DynamicApplication> RenderApp() =>
		Render<DynamicApplication>(p => p.Add(c => c.Slug, "hello-ui"));

	[Test]
	public async Task OnAStockBuild_SaysTheBuildLacksComponentSupport_AndFetchesNoAssembly()
	{
		var requests = Seed(buildSupportsComponents: false);

		var cut = RenderApp();
		cut.WaitForState(() => cut.Markup.Contains("NavApplicationComponentUnsupportedBuild"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).DoesNotContain("NavApplicationComponentLoadFailed");
		await Assert.That(requests).DoesNotContain("/api/plugins/hello/ui/Hello.dll");
	}

	[Test]
	public async Task OnASupportingBuild_AFailedFetchStillReportsTheLoadFailure()
	{
		var requests = Seed(buildSupportsComponents: true);

		var cut = RenderApp();
		cut.WaitForState(() => cut.Markup.Contains("NavApplicationComponentLoadFailed"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).DoesNotContain("NavApplicationComponentUnsupportedBuild");
		await Assert.That(requests).Contains("/api/plugins/hello/ui/Hello.dll");
	}
}
