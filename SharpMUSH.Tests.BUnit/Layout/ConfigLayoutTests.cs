using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Answers each request from its method and the path it asked for.</summary>
file sealed class RouteHandler(Func<HttpMethod, string, HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(respond(request.Method, request.RequestUri!.AbsolutePath.TrimStart('/')));
}

/// <summary>
/// ConfigLayout hosts the D1 sidebar beside the page body and hands it the per-section counts it
/// derives from the shipped schema.
/// </summary>
public class ConfigLayoutTests : TrackingBunitContext
{
	/// <summary>The real schema and defaults, serialized the way /api/configuration serves them.</summary>
	public static string RealConfigurationJson() => JsonSerializer.Serialize(new ConfigurationResponse
	{
		Configuration = SharpMUSHOptions.Default(),
		Schema = SchemaBuilder.BuildSchema()
	});

	private void AddServices(Func<HttpMethod, string, HttpResponseMessage> respond)
	{
		var client = Track(new HttpClient(new RouteHandler(respond)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AdminConfigService>()
			.AddSingleton<ConfigSchemaService>()
			.AddSingleton<SidebarCollapseService>()
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static HttpResponseMessage Json(string json) =>
		new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

	[Test]
	public async Task HostsTheSidebar_AndBody_AndPassesCounts()
	{
		AddServices((_, _) => Json(RealConfigurationJson()));
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/admin/config/chat");
		var expectedChat = SchemaBuilder.BuildSchema().Properties.Values.Count(p => p.Category.Equals("Chat", StringComparison.OrdinalIgnoreCase));

		var cut = Render<PageSidebarHost>(h => h.AddChildContent<ConfigLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));

		cut.WaitForAssertion(() => cut.Find("a[href='/admin/config/chat'] .kit-row-count"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a[href='/admin/config/chat'] .kit-row-count").TextContent).IsEqualTo(expectedChat.ToString());
		await Assert.That(cut.Find(".kit-section-body.config-shell #body")).IsNotNull();
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar.config-shell")).IsNotNull().Because("the sidebar renders in the shell's page-sidebar slot");
	}

	[Test]
	public async Task WithoutTheSchema_TheSidebarStillListsEverySection_WithoutCounts()
	{
		AddServices((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/admin/config");

		var cut = Render<PageSidebarHost>(h => h.AddChildContent<ConfigLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "x"))));

		await Assert.That(cut.FindAll("details.config-side-group").Count).IsEqualTo(6);
		await Assert.That(cut.FindAll(".kit-row-count").Count).IsEqualTo(0);
	}
}
