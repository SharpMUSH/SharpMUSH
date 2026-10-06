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

/// <summary>Answers immediately, or waits on the held completion source when a test holds the reply.</summary>
file sealed class HoldableHandler(ConfigIndexTests.Server server, Func<HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		server.Hold?.Task ?? Task.FromResult(respond());
}

/// <summary>
/// The D1 configuration home (board 27): linked group cards with counts from the real schema, the
/// Important tag on Security, section chips, and an aside of the admin tools the viewer may use.
/// </summary>
public class ConfigIndexTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	/// <summary>Whether the fake server answers at all; a test may hold the answer to see the page mid-load.</summary>
	internal sealed class Server
	{
		public TaskCompletionSource<HttpResponseMessage>? Hold { get; set; }
	}

	private readonly Server _server = new();

	public ConfigIndexTests()
	{
		var client = Track(new HttpClient(new HoldableHandler(_server, () => new HttpResponseMessage(HttpStatusCode.OK)
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
	public async Task CardsRenderWhileTheSchemaIsStillLoading()
	{
		_server.Hold = new TaskCompletionSource<HttpResponseMessage>();
		var cut = Render<ConfigIndex>();
		await Assert.That(cut.FindAll("a.config-cat-card").Count).IsEqualTo(6)
			.Because("the six groups need nothing from the schema; only the counts do");
		await Assert.That(cut.FindAll(".config-cat-count").Count).IsEqualTo(0);
		_server.Hold.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(ConfigLayoutTests.RealConfigurationJson(), Encoding.UTF8, "application/json")
		});
		cut.WaitForAssertion(() => cut.Find(".config-cat-count"), TimeSpan.FromSeconds(5));
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
		var expected = Count("Cosmetic") + Count("Chat") + wiki;

		var cut = RenderHome();

		var count = cut.Find("a.config-cat-card[href='/admin/config/cosmetic'] .config-cat-count").TextContent;
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

	/// <summary>
	/// The aside explains how changes apply and nothing else: it used to carry a third list of staff
	/// pages beside the sidebar and the overview, and the three disagreed.
	/// </summary>
	[Test]
	public async Task AsideOnlyExplainsHowChangesApply()
	{
		Auth.SetPolicies("layout.admin", "packages.admin", "roles.admin");
		var cut = RenderHome();
		await Assert.That(cut.Find(".config-home-aside .config-home-how")).IsNotNull();
		await Assert.That(cut.FindAll(".config-home-aside a").Count).IsEqualTo(0);
	}
}
