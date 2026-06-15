using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Shape tests for <see cref="SchemaViewRenderer"/>: each JSON schema shape (sections, the display
/// element kinds, ordering, per-viewer visibility) must produce the expected Blazor markup/layout.
/// </summary>
public class SchemaViewRendererShapeTests : BunitContext
{
	public SchemaViewRendererShapeTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	// ── builders ───────────────────────────────────────────────────────────

	private static JsonElement El(object? value) => JsonSerializer.SerializeToElement(value);

	private static SchemaData Data(params (string Key, object? Value, bool Visible)[] fields) =>
		new(fields.ToDictionary(f => f.Key, f => new SchemaFieldValue(El(f.Value), f.Visible)));

	private static PortalSchemaDocument View(string? title, params SchemaPage[] pages) =>
		new("view", 1, title, null, pages, null);

	private static SchemaPage Page(int order, string? title, params SchemaSection[] sections) =>
		new($"p{order}", title, order, sections, null, null);

	private static SchemaSection Section(int order, string? name, params SchemaElement[] elements) =>
		new(name, order, null, elements);

	private static SchemaSection Columns(int order, string? name, int columns, params SchemaElement[] elements) =>
		new(name, order, null, elements, columns);

	private IRenderedComponent<SchemaViewRenderer> RenderView(PortalSchemaDocument doc, SchemaData? data = null) =>
		Render<SchemaViewRenderer>(p => p.Add(x => x.Document, doc).Add(x => x.Data, data));

	// ── tests ──────────────────────────────────────────────────────────────

	[TUnit.Core.Test]
	public async Task NullDocument_RendersNothingToDisplay()
	{
		var cut = Render<SchemaViewRenderer>(p => p.Add(x => x.Document, (PortalSchemaDocument?)null));
		await Assert.That(cut.Markup).Contains("Nothing to display");
	}

	[TUnit.Core.Test]
	public async Task Title_RendersAsHeading()
	{
		var doc = View("Character Profile", Page(1, null,
			Section(1, "About", new SchemaElement(Kind: "field", Key: "n", Label: "Name", Type: "text"))));
		var cut = RenderView(doc, Data(("n", "Gandalf", true)));

		await Assert.That(cut.Markup).Contains("Character Profile");
		await Assert.That(cut.Markup).Contains("Name");
		await Assert.That(cut.Markup).Contains("Gandalf");
	}

	[TUnit.Core.Test]
	public async Task Section_RendersAsPaper_AndHidesWhenAllFieldsInvisible()
	{
		var doc = View(null,
			Page(1, null,
				Section(1, "Public", new SchemaElement(Kind: "field", Key: "pub", Label: "Public", Type: "text")),
				Section(2, "Hidden", new SchemaElement(Kind: "field", Key: "sec", Label: "Secret", Type: "text"))));
		var cut = RenderView(doc, Data(("pub", "shown", true), ("sec", "nope", false)));

		// Visible section rendered as a paper; the all-invisible section produces no paper/header.
		await Assert.That(cut.FindAll("div.mud-paper").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("Public");
		await Assert.That(cut.Markup).Contains("shown");
		await Assert.That(cut.Markup).DoesNotContain("Secret");
		await Assert.That(cut.Markup).DoesNotContain("nope");
	}

	[TUnit.Core.Test]
	public async Task Sections_RenderInOrderField()
	{
		var doc = View(null, Page(1, null,
			Section(2, "Second", new SchemaElement(Kind: "field", Key: "b", Label: "B", Type: "text")),
			Section(1, "First", new SchemaElement(Kind: "field", Key: "a", Label: "A", Type: "text"))));
		var cut = RenderView(doc, Data(("a", "x", true), ("b", "y", true)));

		await Assert.That(cut.Markup.IndexOf("First", StringComparison.Ordinal))
			.IsLessThan(cut.Markup.IndexOf("Second", StringComparison.Ordinal));
	}

	[TUnit.Core.Test]
	public async Task MultiPage_ShowsPageTitles_InOrder()
	{
		var doc = View(null,
			Page(2, "Stats", Section(1, null, new SchemaElement(Kind: "field", Key: "s", Label: "Str", Type: "text"))),
			Page(1, "Bio", Section(1, null, new SchemaElement(Kind: "field", Key: "b", Label: "Bio", Type: "text"))));
		var cut = RenderView(doc, Data(("s", "10", true), ("b", "hero", true)));

		await Assert.That(cut.Markup).Contains("Bio");
		await Assert.That(cut.Markup).Contains("Stats");
		await Assert.That(cut.Markup.IndexOf("Bio", StringComparison.Ordinal))
			.IsLessThan(cut.Markup.IndexOf("Stats", StringComparison.Ordinal));
	}

	[TUnit.Core.Test]
	public async Task Divider_RendersHr()
	{
		var doc = View(null, Page(1, null, Section(1, "S", new SchemaElement(Kind: "divider"))));
		var cut = RenderView(doc);
		await Assert.That(cut.FindAll("hr.mud-divider").Count).IsGreaterThanOrEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task Markdown_RendersHtml()
	{
		var doc = View(null, Page(1, null,
			Section(1, "S", new SchemaElement(Kind: "markdown", Value: "## Heading\n\nSome **bold** text."))));
		var cut = RenderView(doc);

		await Assert.That(cut.FindAll("h2").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Heading");
		await Assert.That(cut.FindAll("strong").Count).IsGreaterThanOrEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task Image_RendersImgFromDataField()
	{
		var doc = View(null, Page(1, null,
			Section(1, "S", new SchemaElement(Kind: "image", SrcField: "portrait", Alt: "Portrait"))));
		var cut = RenderView(doc, Data(("portrait", "/files/gandalf.png", true)));

		var img = cut.Find("img");
		await Assert.That(img.GetAttribute("src")).IsEqualTo("/files/gandalf.png");
		await Assert.That(img.GetAttribute("alt")).IsEqualTo("Portrait");
	}

	[TUnit.Core.Test]
	public async Task KeyValue_RendersTableRows_ForVisibleKeysOnly()
	{
		var doc = View(null, Page(1, null,
			Section(1, "Demographics", new SchemaElement(Kind: "keyvalue", Fields: ["fullname", "alias", "secret"]))));
		var cut = RenderView(doc, Data(
			("fullname", "Gandalf the Grey", true),
			("alias", "Mithrandir", true),
			("secret", "Olorin", false)));

		await Assert.That(cut.FindAll("table").Count).IsGreaterThanOrEqualTo(1);
		// Two visible keys → two rows; the hidden one is absent.
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(2);
		await Assert.That(cut.Markup).Contains("Gandalf the Grey");
		await Assert.That(cut.Markup).Contains("Mithrandir");
		await Assert.That(cut.Markup).DoesNotContain("Olorin");
	}

	[TUnit.Core.Test]
	public async Task Table_RendersColumnsAndRows_FromArrayData()
	{
		var rows = new[]
		{
			new Dictionary<string, object> { ["item"] = "Sword", ["qty"] = 1 },
			new Dictionary<string, object> { ["item"] = "Potion", ["qty"] = 3 }
		};
		var doc = View(null, Page(1, null,
			Section(1, "Inventory", new SchemaElement(
				Kind: "table", RowsField: "inv",
				Columns: [new SchemaColumn("item", "Item"), new SchemaColumn("qty", "Qty")]))));
		var cut = RenderView(doc, Data(("inv", rows, true)));

		// Header columns.
		var headers = cut.FindAll("thead th").Select(th => th.TextContent.Trim()).ToList();
		await Assert.That(headers).Contains("Item");
		await Assert.That(headers).Contains("Qty");
		// Two data rows.
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(2);
		await Assert.That(cut.Markup).Contains("Sword");
		await Assert.That(cut.Markup).Contains("Potion");
	}

	[TUnit.Core.Test]
	public async Task NumberValue_RendersAsText()
	{
		var doc = View(null, Page(1, null,
			Section(1, "Stats", new SchemaElement(Kind: "field", Key: "str", Label: "Strength", Type: "number"))));
		var cut = RenderView(doc, Data(("str", 14, true)));

		await Assert.That(cut.Markup).Contains("Strength");
		await Assert.That(cut.Markup).Contains("14");
	}

	[TUnit.Core.Test]
	public async Task DefaultColumns_StacksElements_NoGrid()
	{
		var doc = View(null, Page(1, null, Section(1, "S",
			new SchemaElement(Kind: "field", Key: "a", Label: "A", Type: "text"),
			new SchemaElement(Kind: "field", Key: "b", Label: "B", Type: "text"))));
		var cut = RenderView(doc, Data(("a", "1", true), ("b", "2", true)));

		// Per-row default: no grid.
		await Assert.That(cut.FindAll("div.mud-grid").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task TwoColumns_RendersGrid_WithHalfWidthItems()
	{
		var doc = View(null, Page(1, null, Columns(1, "S", 2,
			new SchemaElement(Kind: "field", Key: "a", Label: "A", Type: "text"),
			new SchemaElement(Kind: "field", Key: "b", Label: "B", Type: "text"))));
		var cut = RenderView(doc, Data(("a", "1", true), ("b", "2", true)));

		await Assert.That(cut.FindAll("div.mud-grid").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll("div.mud-grid-item").Count).IsEqualTo(2);
		// 12 / 2 columns = md-6 per item.
		await Assert.That(cut.FindAll("div.mud-grid-item-md-6").Count).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task ColumnSpan_WidensElementToFullRow()
	{
		var doc = View(null, Page(1, null, Columns(1, "S", 2,
			new SchemaElement(Kind: "field", Key: "wide", Label: "Wide", Type: "text", Span: 2))));
		var cut = RenderView(doc, Data(("wide", "x", true)));

		// span 2 in a 2-column section → md-12 (full width).
		await Assert.That(cut.FindAll("div.mud-grid-item-md-12").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task EmptyValuedField_ShowsByDefault_WithDefaultValue()
	{
		// ShowWhenEmpty defaults true: a present, visible, but empty field still renders, showing its
		// Default ("default value") in place of the empty value.
		var doc = View(null, Page(1, null,
			Section(1, "S", new SchemaElement(Kind: "field", Key: "blank", Label: "Blank", Type: "text", Default: El("—")))));
		var cut = RenderView(doc, Data(("blank", "", true)));

		await Assert.That(cut.FindAll("div.mud-paper").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("Blank");
		await Assert.That(cut.Markup).Contains("—");
	}

	[TUnit.Core.Test]
	public async Task EmptyValuedField_HiddenWhenShowWhenEmptyFalse()
	{
		// Opt out: ShowWhenEmpty=false hides a present-but-empty field.
		var doc = View(null, Page(1, null,
			Section(1, "S", new SchemaElement(Kind: "field", Key: "blank", Label: "Blank", Type: "text", ShowWhenEmpty: false))));
		var cut = RenderView(doc, Data(("blank", "", true)));

		await Assert.That(cut.FindAll("div.mud-paper").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).DoesNotContain("Blank");
	}
}
