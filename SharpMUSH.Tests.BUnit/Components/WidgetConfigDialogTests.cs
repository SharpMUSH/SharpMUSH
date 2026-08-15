using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Pages.Admin.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The layout editor's config dialog is where an admin actually types widget config, so the accepted
/// keys have to be visible there. These confirm the reference table is generated from the widget's
/// <c>ConfigType</c> and that the template button seeds the editor.
/// </summary>
public class WidgetConfigDialogTests : BunitContext
{
	public WidgetConfigDialogTests()
	{
		var registry = new WidgetRegistry();
		registry.Register(new WikiBodyWidgetDescriptor());
		registry.Register(new QuickLinksWidgetDescriptor());
		registry.Register(new StatsWidgetDescriptor());

		Services.AddMudServices();
		Services.AddSingleton<IWidgetRegistry>(registry);
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>
	/// Shows the dialog through the real dialog service inside a provider — a MudDialog renders into
	/// its provider, so rendering the component standalone yields empty markup.
	/// </summary>
	private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
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

		await provider.InvokeAsync(() => service.ShowAsync<WidgetConfigDialog>(widgetKey, parameters));
		return provider;
	}

	[TUnit.Core.Test]
	public async Task ConfigurableWidget_ListsItsKeysAndDefaults()
	{
		var markup = (await RenderDialogAsync("WikiBody")).Markup;

		await Assert.That(markup).Contains("slug");
		await Assert.That(markup).Contains("namespace");
		await Assert.That(markup).Contains("character");
		// The documented default, not the record's null.
		await Assert.That(markup).Contains("\"main\"");
		// Descriptions come from the resx; the echo localizer proves the key is wired through.
		await Assert.That(markup).Contains("LayCfgWikiBodySlug");
	}

	[TUnit.Core.Test]
	public async Task NestedListKeys_AreListedUnderTheirParent()
	{
		var markup = (await RenderDialogAsync("QuickLinks")).Markup;

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

		cut.FindAll("button").First(b => b.TextContent.Contains("LayCfgInsertTemplate")).Click();

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

		var button = cut.FindAll("button").First(b => b.TextContent.Contains("LayCfgInsertTemplate"));
		await Assert.That(button.HasAttribute("disabled")).IsTrue();
	}
}
