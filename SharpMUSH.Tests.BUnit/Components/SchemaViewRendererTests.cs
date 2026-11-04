using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Verifies the read-only schema renderer shows field values from the data payload and honors the
/// softcode-owned per-viewer <c>visible</c> flag (a hidden datum does not render) — the same policy
/// the character profile uses.
/// </summary>
public class SchemaViewRendererTests : BunitContext
{
	public SchemaViewRendererTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

	private static PortalSchemaDocument ViewDoc() => new(
		Kind: "view",
		SchemaVersion: 1,
		Title: "Profile",
		DataSource: null,
		Pages:
		[
			new SchemaPage("p1", null, 1,
			[
				new SchemaSection("Demographics", 1, null,
				[
					new SchemaElement(Kind: "field", Key: "fullname", Label: "Full Name", Type: "text"),
					new SchemaElement(Kind: "field", Key: "secret", Label: "Secret", Type: "text")
				])
			], null, null)
		],
		Actions: null);

	[TUnit.Core.Test]
	public async Task RendersVisibleValues_AndHidesInvisibleOnes()
	{
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue>
		{
			["fullname"] = new(Str("Gandalf the Grey"), Visible: true),
			["secret"] = new(Str("Olorin"), Visible: false)
		});

		var cut = Render<SchemaViewRenderer>(parameters => parameters
			.Add(p => p.Document, ViewDoc())
			.Add(p => p.Data, data));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Gandalf the Grey");
		await Assert.That(markup).Contains("Full Name");
		// The invisible field's value and (since it is the only datum on that field) its label must not show.
		await Assert.That(markup).DoesNotContain("Olorin");
	}

	private static PortalSchemaDocument TwoPages(string? layout) => new(
		Kind: "view",
		SchemaVersion: 1,
		Title: null,
		DataSource: null,
		Pages:
		[
			new SchemaPage("sheet", "Sheet", 1, [new SchemaSection(null, 1, null, [new SchemaElement(Kind: "markdown", Value: "First page")])], null, null),
			new SchemaPage("gear", "Gear", 2, [new SchemaSection(null, 1, null, [new SchemaElement(Kind: "markdown", Value: "Second page")])], null, null),
		],
		Actions: null,
		Layout: layout);

	[TUnit.Core.Test]
	public async Task A_tabbed_view_shows_one_page_under_a_tab_per_page()
	{
		var cut = Render<SchemaViewRenderer>(parameters => parameters.Add(p => p.Document, TwoPages("tabs")));

		var tabs = cut.FindAll(".mud-tab");
		await Assert.That(tabs.Count).IsEqualTo(2);
		await Assert.That(tabs[0].TextContent).Contains("Sheet");
		await Assert.That(tabs[1].TextContent).Contains("Gear");
		await Assert.That(cut.Markup).Contains("First page");
		await Assert.That(cut.Markup).DoesNotContain("Second page");
		await Assert.That(cut.FindAll(".schema-page-title").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task A_view_without_a_layout_stacks_its_pages_under_titles()
	{
		var cut = Render<SchemaViewRenderer>(parameters => parameters.Add(p => p.Document, TwoPages(null)));

		await Assert.That(cut.FindAll(".mud-tab").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".schema-page-title").Count).IsEqualTo(2);
		await Assert.That(cut.Markup).Contains("Second page");
	}
}
