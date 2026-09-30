using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models.Configuration;
using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Layout;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Answers each request from its method and the path it asked for.</summary>
file sealed class RouteHandler(Func<HttpMethod, string, HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(respond(request.Method, request.RequestUri!.AbsolutePath.TrimStart('/')));
}

/// <summary>
/// The D1 configuration home (board 27): linked group cards with counts from the real schema, the
/// Important tag on Security, section chips, and an aside of the admin tools the viewer may use.
/// </summary>
public class ConfigIndexTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public ConfigIndexTests()
	{
		var client = Track(new HttpClient(new RouteHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(ConfigLayoutTests.RealConfigurationJson(), Encoding.UTF8, "application/json")
		}))
		{ BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<ConfigSchemaService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Auth = AddAuthorization();
		Auth.SetAuthorized("admin");
	}

	private IRenderedComponent<ConfigIndex> RenderHome()
	{
		var cut = Render<ConfigIndex>();
		cut.WaitForAssertion(() => cut.Find(".config-cat-count"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task CardsAreLinks_ToTheGroupsFirstSection()
	{
		var cut = RenderHome();
		foreach (var group in ConfigSections.Groups)
		{
			await Assert.That(cut.Find($"a.config-cat-card[href='{group.FirstRoute}']")).IsNotNull().Because(group.Key);
		}
		await Assert.That(cut.Find(".kit-page-head h1")).IsNotNull();
	}

	[Test]
	public async Task ContentCountsIncludeWiki()
	{
		var schema = SchemaBuilder.BuildSchema();
		int Count(string category) => schema.Properties.Values.Count(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
		var wiki = Count("Wiki");
		await Assert.That(wiki).IsGreaterThan(0).Because("the bug only shows when Wiki has settings to count");
		var expected = Count("Message") + Count("Cosmetic") + Count("Chat") + wiki;

		var cut = RenderHome();

		var count = cut.Find("a.config-cat-card[href='/admin/config/message'] .config-cat-count").TextContent;
		await Assert.That(count).Contains(expected.ToString());
	}

	[Test]
	public async Task SecurityCarriesTheImportantTag_AndOnlySecurity()
	{
		var cut = RenderHome();
		await Assert.That(cut.Find("a.config-cat-card[href='/admin/config/sitelock'] .config-cat-flag").TextContent).Contains("Important");
		await Assert.That(cut.FindAll(".config-cat-flag").Count).IsEqualTo(1);
	}

	[Test]
	public async Task AdvancedShowsFourChipsThenMore()
	{
		var cut = RenderHome();
		var card = cut.Find("a.config-cat-card[href='/admin/config/attribute']");
		await Assert.That(card.QuerySelectorAll(".config-cat-chip").Length).IsEqualTo(4);
		await Assert.That(card.QuerySelector(".config-cat-more")!.TextContent).Contains("4");
		var server = cut.Find("a.config-cat-card[href='/admin/config/net']");
		await Assert.That(server.QuerySelectorAll(".config-cat-more").Length).IsEqualTo(0);
	}

	[Test]
	public async Task AsideListsOnlyAuthorisedTools()
	{
		Auth.SetPolicies("layout.admin");
		var cut = RenderHome();
		await Assert.That(cut.Find(".config-home-aside a[href='/admin/layout']")).IsNotNull();
		await Assert.That(cut.FindAll(".config-home-aside a[href='/admin/packages']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".config-home-aside a[href='/admin/roles']").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".config-home-aside .config-home-how")).IsNotNull();
	}
}
