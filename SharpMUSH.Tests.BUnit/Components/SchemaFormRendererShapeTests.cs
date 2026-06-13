using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Hosts a component alongside a MudPopoverProvider (required by MudSelect/MudDatePicker).</summary>
internal sealed class MudHarness : ComponentBase
{
	[Parameter] public RenderFragment? ChildContent { get; set; }

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenComponent<MudPopoverProvider>(0);
		builder.CloseComponent();
		if (ChildContent is not null)
		{
			builder.AddContent(1, ChildContent);
		}
	}
}

/// <summary>
/// Shape tests for <see cref="SchemaFormRenderer"/>: each field <c>type</c> maps to the expected
/// MudBlazor control, and the document shape drives the expected layout (title, step indicator,
/// page nav, submit button, display elements). No network is exercised — rendering only.
/// </summary>
public class SchemaFormRendererShapeTests : BunitContext
{
	public SchemaFormRendererShapeTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(new HttpClient { BaseAddress = new Uri("https://localhost:8081/") });

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(
				sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance));

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	// ── builders ───────────────────────────────────────────────────────────

	private static PortalSchemaDocument Form(string? title, IReadOnlyDictionary<string, SchemaAction>? actions,
		params SchemaPage[] pages) => new("form", 1, title, null, pages, actions);

	private static SchemaPage Page(int order, string? title, params SchemaElement[] elements) =>
		new($"p{order}", title, order, [new SchemaSection("Fields", 1, null, elements)], null, null);

	private static SchemaPage ColumnsPage(int columns, params SchemaElement[] elements) =>
		new("p1", null, 1, [new SchemaSection("Fields", 1, null, elements, columns)], null, null);

	private static SchemaElement Field(string key, string label, string type,
		IReadOnlyList<SchemaOption>? options = null, SchemaValidation? validation = null) =>
		new(Kind: "field", Key: key, Label: label, Type: type, Options: options, Validation: validation);

	private static readonly Dictionary<string, SchemaAction> SubmitOnly = new()
	{
		["submit"] = new("http", "POST", "/http/x/submit", "fields", null, new SchemaActionError(true))
	};

	// MudSelect / MudDatePicker require a MudPopoverProvider in the render tree, so host the form
	// inside MudHarness. FindAll/Markup operate over the whole tree (the provider contributes only
	// an empty container, so form assertions are unaffected).
	private IRenderedComponent<MudHarness> RenderForm(PortalSchemaDocument doc) =>
		Render<MudHarness>(p => p.AddChildContent<SchemaFormRenderer>(cp => cp.Add(x => x.Document, doc)));

	// ── field type → control ─────────────────────────────────────────────────

	[TUnit.Core.Test]
	public async Task TextField_RendersInput()
	{
		var cut = RenderForm(Form(null, null, Page(1, null, Field("name", "Character Name", "text"))));
		await Assert.That(cut.Markup).Contains("Character Name");
		await Assert.That(cut.FindAll("input").Count).IsGreaterThanOrEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task TextareaAndMstring_RenderTextarea()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("bio", "Background", "textarea"),
			Field("desc", "Description", "mstring"))));
		await Assert.That(cut.FindAll("textarea").Count).IsGreaterThanOrEqualTo(2);
		await Assert.That(cut.Markup).Contains("Background");
		await Assert.That(cut.Markup).Contains("Description");
	}

	[TUnit.Core.Test]
	public async Task BooleanField_RendersSwitch()
	{
		var cut = RenderForm(Form(null, null, Page(1, null, Field("active", "Active", "boolean"))));
		await Assert.That(cut.FindAll(".mud-switch").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Active");
	}

	[TUnit.Core.Test]
	public async Task SliderField_RendersSlider()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("str", "Strength", "slider", validation: new SchemaValidation(false, 3, 18, null, null)))));
		await Assert.That(cut.FindAll(".mud-slider").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Strength");
	}

	[TUnit.Core.Test]
	public async Task SelectField_RendersSelect()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("class", "Class", "select",
				options: [new SchemaOption("fighter", "Fighter"), new SchemaOption("wizard", "Wizard")]))));
		await Assert.That(cut.FindAll(".mud-select").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Class");
	}

	[TUnit.Core.Test]
	public async Task MultiSelectField_RendersSelect()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("tags", "Tags", "multiselect",
				options: [new SchemaOption("a", "Alpha"), new SchemaOption("b", "Beta")]))));
		await Assert.That(cut.FindAll(".mud-select").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Tags");
	}

	[TUnit.Core.Test]
	public async Task RadioField_RendersRadiosWithOptionsInline()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("align", "Alignment", "radio",
				options: [new SchemaOption("good", "Good"), new SchemaOption("evil", "Evil")]))));
		await Assert.That(cut.FindAll(".mud-radio").Count).IsGreaterThanOrEqualTo(2);
		// Radio option labels render inline (unlike select items, which live in a popover).
		await Assert.That(cut.Markup).Contains("Good");
		await Assert.That(cut.Markup).Contains("Evil");
	}

	[TUnit.Core.Test]
	public async Task DateField_RendersPicker()
	{
		var cut = RenderForm(Form(null, null, Page(1, null, Field("born", "Born", "date"))));
		await Assert.That(cut.FindAll(".mud-picker").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.Markup).Contains("Born");
	}

	[TUnit.Core.Test]
	public async Task HiddenField_IsNotRendered()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("token", "Secret Token", "hidden"),
			Field("name", "Name", "text"))));
		await Assert.That(cut.Markup).DoesNotContain("Secret Token");
		await Assert.That(cut.Markup).Contains("Name");
	}

	// ── layout / navigation ──────────────────────────────────────────────────

	[TUnit.Core.Test]
	public async Task Title_RendersAsHeading()
	{
		var cut = RenderForm(Form("Character Generation", SubmitOnly, Page(1, null, Field("n", "Name", "text"))));
		await Assert.That(cut.Markup).Contains("Character Generation");
	}

	[TUnit.Core.Test]
	public async Task SinglePageWithSubmit_ShowsSubmitButton_NoNav()
	{
		var cut = RenderForm(Form(null, SubmitOnly, Page(1, null, Field("n", "Name", "text"))));
		var buttons = cut.FindAll("button").Select(b => b.TextContent).ToList();
		await Assert.That(buttons.Any(t => t.Contains("Submit"))).IsTrue();
		await Assert.That(buttons.Any(t => t.Contains("Next"))).IsFalse();
		await Assert.That(buttons.Any(t => t.Contains("Back"))).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task MultiPage_FirstPage_ShowsStepIndicatorAndNext_NoBack()
	{
		var cut = RenderForm(Form(null, SubmitOnly,
			Page(1, "Basics", Field("n", "Name", "text")),
			Page(2, "Stats", Field("s", "Str", "number"))));

		await Assert.That(cut.Markup).Contains("Step 1 of 2");
		var buttons = cut.FindAll("button").Select(b => b.TextContent).ToList();
		await Assert.That(buttons.Any(t => t.Contains("Next"))).IsTrue();
		await Assert.That(buttons.Any(t => t.Contains("Back"))).IsFalse();
		// First page fields visible; second page fields not yet.
		await Assert.That(cut.Markup).Contains("Name");
		await Assert.That(cut.Markup).DoesNotContain("Str");
	}

	[TUnit.Core.Test]
	public async Task MultiPage_AdvancesToSecondPage_OnNext()
	{
		var cut = RenderForm(Form(null, SubmitOnly,
			Page(1, "Basics", Field("n", "Name", "text")),
			Page(2, "Stats", Field("s", "Strength", "number"))));

		cut.FindAll("button").First(b => b.TextContent.Contains("Next")).Click();

		await Assert.That(cut.Markup).Contains("Step 2 of 2");
		await Assert.That(cut.Markup).Contains("Strength");
		var buttons = cut.FindAll("button").Select(b => b.TextContent).ToList();
		await Assert.That(buttons.Any(t => t.Contains("Back"))).IsTrue();
		await Assert.That(buttons.Any(t => t.Contains("Submit"))).IsTrue();
	}

	// ── display elements inside a form ────────────────────────────────────────

	[TUnit.Core.Test]
	public async Task DisplayElements_MarkdownDividerButton_Render()
	{
		var actions = new Dictionary<string, SchemaAction>
		{
			["roll"] = new("http", "POST", "/http/x/roll", "fields", null, null)
		};
		var doc = Form(null, actions, Page(1, null,
			new SchemaElement(Kind: "markdown", Value: "## Welcome"),
			new SchemaElement(Kind: "divider"),
			new SchemaElement(Kind: "button", Label: "Roll Stats", Action: "roll")));
		var cut = RenderForm(doc);

		await Assert.That(cut.FindAll("h2").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll("hr.mud-divider").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("Roll Stats"))).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task DefaultColumns_StacksFields_NoGrid()
	{
		var cut = RenderForm(Form(null, null, Page(1, null,
			Field("a", "A", "text"), Field("b", "B", "text"))));
		await Assert.That(cut.FindAll("div.mud-grid").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task TwoColumns_RendersGrid_WithHalfWidthItems()
	{
		var cut = RenderForm(Form(null, null, ColumnsPage(2,
			Field("a", "A", "text"), Field("b", "B", "text"))));
		await Assert.That(cut.FindAll("div.mud-grid").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll("div.mud-grid-item").Count).IsEqualTo(2);
		await Assert.That(cut.FindAll("div.mud-grid-item-md-6").Count).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task ColumnSpan_WidensFieldToFullRow()
	{
		var cut = RenderForm(Form(null, null, ColumnsPage(2,
			new SchemaElement(Kind: "field", Key: "bio", Label: "Bio", Type: "textarea", Span: 2))));
		await Assert.That(cut.FindAll("div.mud-grid-item-md-12").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task NoPages_ShowsEmptyMessage()
	{
		var cut = RenderForm(Form("Empty", null));
		await Assert.That(cut.Markup).Contains("no pages");
	}

	[TUnit.Core.Test]
	public async Task AllFieldTypes_RenderTheirControls_Together()
	{
		var doc = Form(null, null, Page(1, null,
			Field("t", "T", "text"),
			Field("a", "A", "textarea"),
			Field("n", "N", "number"),
			Field("b", "B", "boolean"),
			Field("sl", "SL", "slider"),
			Field("se", "SE", "select", options: [new SchemaOption("x", "X")]),
			Field("r", "R", "radio", options: [new SchemaOption("y", "Y")]),
			Field("d", "D", "date")));
		var cut = RenderForm(doc);

		await Assert.That(cut.FindAll("textarea").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll(".mud-switch").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll(".mud-slider").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll(".mud-select").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll(".mud-radio").Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(cut.FindAll(".mud-picker").Count).IsGreaterThanOrEqualTo(1);
	}
}
