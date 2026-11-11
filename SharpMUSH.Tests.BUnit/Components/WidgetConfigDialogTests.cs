using System.Net;
using NSubstitute;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Pages.Admin.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers the media library's list with the given pictures.</summary>
file sealed class AssetListHandler(WikiAssetInfo[] assets) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(assets) });
}

/// <summary>
/// The layout editor's config dialog is where an admin actually types widget config, so the accepted
/// keys have to be visible there. These confirm the reference table is generated from the widget's
/// <c>ConfigType</c> and that the template button seeds the editor.
/// </summary>
public class WidgetConfigDialogTests : TrackingBunitContext
{
	private const string RulesIcon = "/api/wiki-assets/abc/rules.svg";

	public WidgetConfigDialogTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(BuiltInWidgets.Named("WikiBody"));
		registry.Register(BuiltInWidgets.Named("QuickLinks"));
		registry.Register(BuiltInWidgets.Named("Stats"));
		registry.Register(BuiltInWidgets.Named("WelcomeText"));

		Services.AddMudServices();
		Services.AddSingleton<IWidgetRegistry>(registry);
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		// The link icon field offers the media uploads, which it lists through WikiAssetService.
		var client = Track(new HttpClient(new AssetListHandler(
			[new("abc", "rules.svg", RulesIcon, "image/svg+xml", 10, "", "#1", DateTimeOffset.UtcNow)]))
		{ BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(factory)
			.AddSingleton<ILogger<WikiAssetService>>(NullLogger<WikiAssetService>.Instance)
			.AddSingleton<WikiAssetService>();
		var auth = AddAuthorization();
		auth.SetAuthorized("staff");
		auth.SetPolicies("layout.admin", "media.admin", "media.upload");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>
	/// Shows the dialog through the real dialog service inside a provider — a MudDialog renders into
	/// its provider, so rendering the component standalone yields empty markup.
	/// </summary>
	private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
		string widgetKey, string? initialJson = null)
		=> (await ShowDialogAsync(widgetKey, initialJson)).Provider;

	private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> ShowDialogAsync(
		string widgetKey, string? initialJson = null)
	{
		var provider = Render<MudDialogProvider>();
		var service = Services.GetRequiredService<IDialogService>();

		var parameters = new DialogParameters<WidgetConfigDialog>
		{
			{ x => x.WidgetName, widgetKey },
			{ x => x.WidgetKey, widgetKey },
			{ x => x.InitialJson, initialJson }
		};

		var dialog = await provider.InvokeAsync(() => service.ShowAsync<WidgetConfigDialog>(widgetKey, parameters));
		return (provider, dialog);
	}

	/// <summary>Opens the JSON view from the settings form.</summary>
	private static async Task ShowJsonAsync(IRenderedComponent<MudDialogProvider> provider)
		=> await provider.Find(".wcd-to-json").ClickAsync();

	/// <summary>Clicks Apply and returns what the dialog closed with: the config JSON, or null for none.</summary>
	private static async Task<string?> ApplyAsync(IRenderedComponent<MudDialogProvider> provider, IDialogReference dialog)
	{
		await provider.FindAll("button").First(b => b.TextContent.Contains("LayApply")).ClickAsync();
		var result = await dialog.Result;
		await Assert.That(result!.Canceled).IsFalse();
		return result.Data as string;
	}

	private static System.Text.Json.Nodes.JsonObject Parsed(string? json)
		=> (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json!)!;

	/// <summary>Clicks Apply and returns the dialog markup, which carries any validation error.</summary>
	private static async Task<string> SaveAndRead(IRenderedComponent<MudDialogProvider> provider)
	{
		await provider.FindAll("button").First(b => b.TextContent.Contains("LayApply")).ClickAsync();
		return provider.Markup;
	}

	[TUnit.Core.Test]
	[Arguments("[1, 2, 3]")]
	[Arguments("\"just a string\"")]
	[Arguments("42")]
	[Arguments("null")]
	public async Task NonObjectJson_IsRejected(string json)
	{
		// These all parse, and every widget then ignores them — so saving one would look like it
		// worked and change nothing.
		var provider = await RenderDialogAsync("WikiBody", json);

		await Assert.That(await SaveAndRead(provider)).Contains("LayCfgNotAnObject");
	}

	[TUnit.Core.Test]
	public async Task ObjectJson_IsAccepted()
	{
		var provider = await RenderDialogAsync("WikiBody", "{\"slug\":\"house-rules\"}");

		await Assert.That(await SaveAndRead(provider)).DoesNotContain("LayCfgNotAnObject");
	}

	[TUnit.Core.Test]
	public async Task MalformedJson_StillReportsAParseError()
	{
		var provider = await RenderDialogAsync("WikiBody", "{not json");

		await Assert.That(await SaveAndRead(provider)).Contains("LayInvalidJson");
	}

	[TUnit.Core.Test]
	public async Task JsonView_ListsTheWidgetsKeysAndDefaults()
	{
		var provider = await RenderDialogAsync("WikiBody");
		await ShowJsonAsync(provider);
		var markup = provider.Markup;

		await Assert.That(markup).Contains("slug");
		await Assert.That(markup).Contains("namespace");
		await Assert.That(markup).Contains("character");
		// The documented default, not the record's null.
		await Assert.That(markup).Contains("\"main\"");
		// Descriptions come from the resx; the echo localizer proves the key is wired through.
		await Assert.That(markup).Contains("LayCfgWikiBodySlug");
	}

	[TUnit.Core.Test]
	public async Task JsonView_ListsNestedListKeysUnderTheirParent()
	{
		var provider = await RenderDialogAsync("QuickLinks");
		await ShowJsonAsync(provider);
		var markup = provider.Markup;

		await Assert.That(markup).Contains("links");
		await Assert.That(markup).Contains("newTab");
		await Assert.That(markup).Contains("LayCfgQuickLinkUrl");
	}

	[TUnit.Core.Test]
	public async Task WidgetWithoutConfig_SaysSo()
	{
		var markup = (await RenderDialogAsync("Stats")).Markup;

		await Assert.That(markup).Contains("LayCfgNone");
		await Assert.That(markup).DoesNotContain("LayCfgInsertTemplate");
	}

	[TUnit.Core.Test]
	public async Task InsertTemplate_SeedsTheEditorWithEveryKey()
	{
		var cut = await RenderDialogAsync("WikiBody");
		await ShowJsonAsync(cut);

		await cut.FindAll("button").First(b => b.TextContent.Contains("LayCfgInsertTemplate")).ClickAsync();

		// Read the rendered editor rather than the MudTextField instance — reaching into a component's
		// bound parameter is what MUD0012 warns about, and the DOM is what an admin actually sees.
		var json = cut.Find("textarea").TextContent;
		await Assert.That(json).Contains("\"slug\"");
		await Assert.That(json).Contains("\"namespace\": \"main\"");
	}

	[TUnit.Core.Test]
	public async Task InsertTemplate_IsDisabled_WhenAConfigAlreadyExists()
	{
		// Seeding over an admin's existing config would destroy it.
		var cut = await RenderDialogAsync("WikiBody", "{\"slug\":\"house-rules\"}");
		await ShowJsonAsync(cut);

		var button = cut.FindAll("button").First(b => b.TextContent.Contains("LayCfgInsertTemplate"));
		await Assert.That(button.HasAttribute("disabled")).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task Form_IsWhatOpens_WithAFieldPerKey()
	{
		var provider = await RenderDialogAsync("WikiBody", "{\"slug\":\"house-rules\"}");

		await Assert.That(provider.FindAll(".wcf-field").Select(f => f.GetAttribute("data-config-key")))
			.IsEquivalentTo(["slug", "namespace", "locale", "character"]);
		await Assert.That(provider.Find("[data-config-key='slug'] input").GetAttribute("value")).IsEqualTo("house-rules");
		// The documented default is the placeholder, as text rather than its JSON literal.
		await Assert.That(provider.Find("[data-config-key='namespace'] input").GetAttribute("placeholder")).IsEqualTo("main");
		// An empty field shows a sample value under a raised label.
		await Assert.That(provider.Find("[data-config-key='locale'] input").GetAttribute("placeholder")).IsEqualTo("de");
		await Assert.That(provider.FindAll("[data-config-key='locale'] .mud-shrink").Count).IsEqualTo(1);
		await Assert.That(provider.Markup).Contains("LayCfgWikiBodySlugLabel");
		await Assert.That(provider.Markup).Contains("LayCfgWikiBodySlug");
		await Assert.That(provider.FindAll("textarea").Count).IsEqualTo(0).Because("no raw JSON until asked for");
	}

	[TUnit.Core.Test]
	public async Task Form_SavesWhatWasTyped()
	{
		var (provider, dialog) = await ShowDialogAsync("WikiBody");

		await provider.Find("[data-config-key='slug'] input").InputAsync("house-rules");
		await provider.Find("[data-config-key='namespace'] input").InputAsync("help");

		var saved = Parsed(await ApplyAsync(provider, dialog));
		await Assert.That(saved.Select(p => p.Key)).IsEquivalentTo(["slug", "namespace"]);
		await Assert.That((string?)saved["slug"]).IsEqualTo("house-rules");
		await Assert.That((string?)saved["namespace"]).IsEqualTo("help");
	}

	[TUnit.Core.Test]
	public async Task Form_KeepsKeysItDoesNotShow_AndSaysSo()
	{
		// "Slug" is not the widget's "slug": the widget ignores it, so the form lists it rather than showing it as the page.
		var (provider, dialog) = await ShowDialogAsync("WikiBody", "{\"Slug\":\"old\",\"theme\":{\"dark\":true}}");
		await Assert.That(provider.Find(".wcd-unknown").TextContent).Contains("Slug, theme");
		await Assert.That(provider.Find("[data-config-key='slug'] input").GetAttribute("value") ?? "").IsEqualTo("");

		await provider.Find("[data-config-key='slug'] input").InputAsync("new");

		var saved = Parsed(await ApplyAsync(provider, dialog));
		await Assert.That(saved.Select(p => p.Key)).IsEquivalentTo(["Slug", "theme", "slug"]);
		await Assert.That((string?)saved["slug"]).IsEqualTo("new");
		await Assert.That((bool?)saved["theme"]!["dark"]).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task Form_DropsALinkAddedButNeverFilledIn()
	{
		var (provider, dialog) = await ShowDialogAsync("QuickLinks");

		await provider.Find(".wcf-add").ClickAsync();

		await Assert.That(await ApplyAsync(provider, dialog)).IsNull();
	}

	[TUnit.Core.Test]
	public async Task Form_SwitchNamesItsHelpText()
	{
		var provider = await RenderDialogAsync("WelcomeText");
		var described = provider.Find("[data-config-key='showToGuests'] input").GetAttribute("aria-describedby");

		await Assert.That(described).IsNotNull();
		await Assert.That(provider.Find($"[id='{described}']").TextContent).Contains("LayCfgWelcomeShowToGuests");
	}

	[TUnit.Core.Test]
	public async Task Form_ClearingEveryField_ClearsTheConfig()
	{
		var (provider, dialog) = await ShowDialogAsync("WikiBody", "{\"slug\":\"house-rules\"}");

		await provider.Find("[data-config-key='slug'] input").InputAsync("");

		await Assert.That(await ApplyAsync(provider, dialog)).IsNull();
	}

	[TUnit.Core.Test]
	public async Task Form_EditsAListOfLinks()
	{
		var (provider, dialog) = await ShowDialogAsync("QuickLinks");

		await provider.Find(".wcf-add").ClickAsync();
		await provider.Find(".wcf-item [data-config-key='label'] input").InputAsync("Rules");
		await provider.Find(".wcf-item [data-config-key='url'] input").InputAsync("/wiki/rules");
		await provider.Find(".wcf-add").ClickAsync();
		await provider.FindAll(".wcf-item")[1].QuerySelector("[data-config-key='label'] input")!.InputAsync("News");
		await provider.FindAll(".wcf-item")[1].QuerySelector("[data-config-key='url'] input")!.InputAsync("https://example.com/news");
		await provider.FindAll(".wcf-item")[1].QuerySelector("[data-config-key='newTab'] input")!.ChangeAsync(true);
		// The second link first.
		await provider.Find("button[aria-label='LayCfgMoveUp(2)']").ClickAsync();

		var links = Parsed(await ApplyAsync(provider, dialog))["links"]!.AsArray();
		await Assert.That(links.Count).IsEqualTo(2);
		await Assert.That((string?)links[0]!["label"]).IsEqualTo("News");
		await Assert.That((bool?)links[0]!["newTab"]).IsTrue();
		await Assert.That((string?)links[1]!["url"]).IsEqualTo("/wiki/rules");
	}

	[TUnit.Core.Test]
	public async Task Form_LinkIcon_IsPickedFromTheMediaUploads()
	{
		var (provider, dialog) = await ShowDialogAsync("QuickLinks", "{\"links\":[{\"label\":\"Rules\",\"url\":\"/wiki/rules\"}]}");
		AngleSharp.Dom.IElement Icon() => provider.Find(".wcf-item [data-config-key='icon']");

		await Assert.That(Icon().QuerySelector(".cfg-image")).IsNotNull().Because("an icon is a picture, not a text field");
		await Assert.That(Icon().QuerySelector("input.cfg-text")!.GetAttribute("placeholder")).IsEqualTo("/api/wiki-assets/7/rules.svg");

		await Icon().QuerySelector("button[aria-expanded]")!.ClickAsync();
		provider.WaitForAssertion(() => _ = Icon().QuerySelector(".cfg-image-tile") ?? throw new InvalidOperationException("no tiles yet"), TimeSpan.FromSeconds(5));
		await Icon().QuerySelector(".cfg-image-tile")!.ClickAsync();

		await Assert.That(Icon().QuerySelector(".cfg-image-preview img")!.GetAttribute("src")).IsEqualTo(RulesIcon);
		var links = Parsed(await ApplyAsync(provider, dialog))["links"]!.AsArray();
		await Assert.That((string?)links[0]!["icon"]).IsEqualTo(RulesIcon);
	}

	[TUnit.Core.Test]
	public async Task Form_LinkIcon_TakesATypedAddress()
	{
		var (provider, dialog) = await ShowDialogAsync("QuickLinks", "{\"links\":[{\"label\":\"Rules\",\"url\":\"/wiki/rules\"}]}");

		await provider.Find(".wcf-item [data-config-key='icon'] input.cfg-text").ChangeAsync("  https://example.com/rules.svg ");

		var links = Parsed(await ApplyAsync(provider, dialog))["links"]!.AsArray();
		await Assert.That((string?)links[0]!["icon"]).IsEqualTo("https://example.com/rules.svg");
	}

	[TUnit.Core.Test]
	public async Task Form_RemovingTheLastLink_DropsTheList()
	{
		var (provider, dialog) = await ShowDialogAsync("QuickLinks", "{\"links\":[{\"label\":\"Rules\",\"url\":\"/wiki/rules\"}]}");

		await provider.Find("button[aria-label='LayCfgRemoveItem(1)']").ClickAsync();

		await Assert.That(provider.FindAll(".wcf-item").Count).IsEqualTo(0);
		await Assert.That(await ApplyAsync(provider, dialog)).IsNull();
	}

	[TUnit.Core.Test]
	public async Task JsonView_AndBack_CarriesTheEdits()
	{
		var (provider, dialog) = await ShowDialogAsync("WikiBody", "{\"slug\":\"house-rules\"}");
		await ShowJsonAsync(provider);

		await provider.Find("textarea").ChangeAsync("{\"slug\":\"faq\",\"locale\":\"de\"}");
		await provider.Find(".wcd-to-form").ClickAsync();

		await Assert.That(provider.Find("[data-config-key='locale'] input").GetAttribute("value")).IsEqualTo("de");
		await Assert.That((string?)Parsed(await ApplyAsync(provider, dialog))["slug"]).IsEqualTo("faq");
	}

	[TUnit.Core.Test]
	public async Task JsonView_KeepsBrokenJson_InsteadOfOpeningTheForm()
	{
		var provider = await RenderDialogAsync("WikiBody");
		await ShowJsonAsync(provider);

		await provider.Find("textarea").ChangeAsync("{not json");
		await provider.Find(".wcd-to-form").ClickAsync();

		await Assert.That(provider.FindAll(".wcf").Count).IsEqualTo(0);
		await Assert.That(provider.Markup).Contains("LayInvalidJson");
	}

	/// <summary>Every text or number key of a shipped widget shows something in its empty field: its default or a sample value.</summary>
	[TUnit.Core.Test]
	public async Task EveryShippedTextOrNumberKey_HasAnExampleForItsEmptyField()
	{
		var missing = (
			from widget in BuiltInWidgets.All
			from field in SharpMUSH.Library.Models.Portal.Widgets.WidgetConfigSchema.Describe(widget.ConfigType)
				.SelectMany(f => f.Children.Prepend(f))
			where field.TypeLabel is "string" or "integer" or "number"
				&& WidgetConfigForm.ExampleText(field) is null
			select $"{widget.Name}.{field.Key}").ToList();

		await Assert.That(missing).IsEmpty();
	}
}
