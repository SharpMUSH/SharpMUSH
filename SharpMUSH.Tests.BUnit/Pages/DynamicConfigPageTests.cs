using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
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
/// The D1 section page (boards 28–29) rendered against the shipped schema and defaults for the Chat
/// category: section labels above cards of rows, the kit controls, the unsaved bottom bar and the
/// "In this section" table of contents.
/// </summary>
public class DynamicConfigPageTests : TrackingBunitContext
{
	private string _json = ConfigLayoutTests.RealConfigurationJson();

	public DynamicConfigPageTests()
	{
		var client = Track(new HttpClient(new RouteHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(_json, Encoding.UTF8, "application/json")
		}))
		{ BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AdminConfigService>()
			.AddSingleton<ConfigSchemaService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/admin/config/chat");
	}

	private IRenderedComponent<DynamicConfig> RenderChat()
	{
		var cut = Render<DynamicConfig>(p => p.Add(x => x.Category, "chat"));
		cut.WaitForAssertion(() => cut.Find(".cfg-row"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private static int ChatGroups()
	{
		var schema = SchemaBuilder.BuildSchema();
		return schema.Properties.Values
			.Where(p => p.Category.Equals("Chat", StringComparison.OrdinalIgnoreCase))
			.Select(p => p.Group ?? "General")
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Count();
	}

	[Test]
	public async Task RendersGroupsAsSectionLabelsWithCards()
	{
		var cut = RenderChat();
		await Assert.That(cut.FindAll(".cfg-group .kit-section-label").Count).IsEqualTo(ChatGroups());
		await Assert.That(cut.FindAll(".cfg-group .kit-card").Count).IsEqualTo(ChatGroups());
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Chat");
	}

	[Test]
	public async Task RowShowsNameKeyAndHelp()
	{
		var cut = RenderChat();
		var row = cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.MaxChannels");
		await Assert.That(row.QuerySelector(".cfg-row-name")!.TextContent).Contains("Max Channels");
		await Assert.That(row.QuerySelector(".cfg-row-help")).IsNotNull();
	}

	[Test]
	public async Task SingleCharPattern_RendersTheNarrowInput_AndNumericShowsItsRange()
	{
		var cut = RenderChat();
		var alias = cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.ChatTokenAlias");
		await Assert.That(alias.QuerySelector("input.cfg-text--char")).IsNotNull()
			.Because("the chat token is one character; README §6.4 keys the narrow input on the ^.$ pattern");
		var max = cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.MaxChannels");
		await Assert.That(max.QuerySelector("input.cfg-num")).IsNotNull();
		await Assert.That(max.QuerySelector(".cfg-range")!.TextContent).Contains("–");
	}

	[Test]
	public async Task Switch_IsARoleSwitch_AndTogglingMarksTheRowChanged_AndShowsTheUnsavedBar()
	{
		var cut = RenderChat();
		var row = cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.NoisyCEmit");
		var toggle = row.QuerySelector("button[role=switch]")!;
		var before = toggle.GetAttribute("aria-checked");
		await Assert.That(cut.FindAll(".cfg-unsaved").Count).IsEqualTo(0);

		toggle.Click();

		row = cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.NoisyCEmit");
		await Assert.That(row.QuerySelector("button[role=switch]")!.GetAttribute("aria-checked")).IsNotEqualTo(before);
		await Assert.That(row.QuerySelector(".cfg-row-changed")).IsNotNull();
		await Assert.That(cut.Find(".cfg-unsaved .cfg-count").TextContent).Contains("1");
		await Assert.That(cut.Find(".cfg-unsaved button.kit-capsule--primary").TextContent).Contains("Save");
	}

	[Test]
	public async Task UnsavedBar_ResetChangesClearsIt()
	{
		var cut = RenderChat();
		cut.FindAll(".cfg-row").First(r => r.QuerySelector(".cfg-row-key")!.TextContent == "Chat.NoisyCEmit").QuerySelector("button[role=switch]")!.Click();
		cut.Find(".cfg-unsaved .cfg-reset").Click();
		await Assert.That(cut.FindAll(".cfg-unsaved").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".cfg-row-changed").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TocListsGroupsWithCounts_AndLinksToThem()
	{
		var cut = RenderChat();
		var toc = cut.FindAll(".cfg-toc");
		await Assert.That(toc.Count).IsEqualTo(ChatGroups());
		await Assert.That(toc[0].GetAttribute("href")).IsEqualTo("#group-general");
		await Assert.That(toc[0].QuerySelector(".cfg-toc-count")!.TextContent).IsEqualTo("2");
		await Assert.That(cut.Find("#group-general")).IsNotNull();
	}

	[Test]
	public async Task FlatCategory_HasOneCardAndNoToc()
	{
		// Every shipped category has groups, so a flat one is built from the real schema with Dump's
		// group removed: one flat card, no table of contents.
		var schema = SchemaBuilder.BuildSchema();
		foreach (var prop in schema.Properties.Values.Where(p => p.Category.Equals("Dump", StringComparison.OrdinalIgnoreCase)))
		{
			prop.Group = null;
		}
		schema.Categories.First(c => c.Name.Equals("Dump", StringComparison.OrdinalIgnoreCase)).Groups.Clear();
		_json = System.Text.Json.JsonSerializer.Serialize(new ConfigurationResponse
		{
			Configuration = SharpMUSH.Configuration.Options.SharpMUSHOptions.Default(),
			Schema = schema
		});
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/admin/config/dump");
		var cut = Render<DynamicConfig>(p => p.Add(x => x.Category, "dump"));
		cut.WaitForAssertion(() => cut.Find(".cfg-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".cfg-group .kit-card").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".cfg-toc").Count).IsEqualTo(0);
	}
}
