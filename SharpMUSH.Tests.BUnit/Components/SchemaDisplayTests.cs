using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Hosts a component beside the MudDialogProvider a confirmation dialog renders into.</summary>
internal sealed class DialogHarness : ComponentBase
{
	[Parameter] public RenderFragment? ChildContent { get; set; }

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenComponent<MudPopoverProvider>(0);
		builder.CloseComponent();
		builder.OpenComponent<MudDialogProvider>(1);
		builder.CloseComponent();
		if (ChildContent is not null)
		{
			builder.AddContent(2, ChildContent);
		}
	}
}

/// <summary>
/// The display elements both renderers share (<see cref="SchemaDisplay"/>): a table's links, chips and
/// empty text; a timeline's markdown and code bodies, tags and actions; a button's confirmation and values.
/// </summary>
public class SchemaDisplayTests : BunitContext
{
	public SchemaDisplayTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static SchemaData Rows(string key, string json) =>
		new(new Dictionary<string, SchemaFieldValue> { [key] = new(JsonDocument.Parse(json).RootElement.Clone()) });

	private static readonly IReadOnlyDictionary<string, SchemaAction> Actions = new Dictionary<string, SchemaAction>
	{
		["comment"] = new("http", "POST", "/http/jobs/comment", "fields", null, null),
		["close"] = new("http", "POST", "/http/jobs/close", "fields", null, null),
	};

	private IRenderedComponent<SchemaDisplay> RenderDisplay(SchemaElement element, SchemaData? data,
		Action<SchemaActionRequest>? onAction = null) =>
		Render<SchemaDisplay>(p => p
			.Add(x => x.Element, element)
			.Add(x => x.Data, data)
			.Add(x => x.Actions, Actions)
			.Add(x => x.OnAction, (SchemaActionRequest request) => onAction?.Invoke(request)));

	private static SchemaElement JobsTable(string? empty = null) => new(
		Kind: "table", RowsField: "jobs", Empty: empty,
		Columns:
		[
			new SchemaColumn("id", "#", Link: "/apps/jobs/{id}"),
			new SchemaColumn("status", "Status", Type: "chip", ColorKey: "status_color"),
			new SchemaColumn("title", "Title"),
		]);

	[Test]
	public async Task A_column_link_fills_its_template_from_the_row_escaped()
	{
		var cut = RenderDisplay(JobsTable(), Rows("jobs", """[{"id":"12 b","status":"Open","status_color":"warning","title":"Help"}]"""));

		var link = cut.Find("a.schema-link");
		await Assert.That(link.GetAttribute("href")).IsEqualTo("/apps/jobs/12%20b");
		await Assert.That(link.TextContent).Contains("12 b");
	}

	[Test]
	public async Task A_link_template_that_would_leave_the_portal_for_a_script_renders_no_anchor()
	{
		var element = new SchemaElement(Kind: "table", RowsField: "jobs",
			Columns: [new SchemaColumn("id", "#", Link: "javascript:alert({id})")]);
		var cut = RenderDisplay(element, Rows("jobs", """[{"id":"1"}]"""));

		await Assert.That(cut.FindAll("a").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("1");
	}

	[Test]
	public async Task A_chip_column_renders_a_chip_in_the_rows_color()
	{
		var cut = RenderDisplay(JobsTable(), Rows("jobs", """
			[{"id":"1","status":"Open","status_color":"warning","title":"a"},
			 {"id":"2","status":"Odd","status_color":"chartreuse","title":"b"}]
			"""));

		var chips = cut.FindAll(".mud-chip");
		await Assert.That(chips.Count).IsEqualTo(2);
		await Assert.That(chips[0].TextContent).Contains("Open");
		await Assert.That(chips[0].ClassName).Contains("warning");
		await Assert.That(chips[1].ClassName).DoesNotContain("chartreuse");
		await Assert.That(chips[1].ClassName).Contains("default");
	}

	[Test]
	public async Task A_table_with_no_rows_shows_its_empty_text()
	{
		var cut = RenderDisplay(JobsTable("No jobs match."), Rows("jobs", "[]"));

		await Assert.That(cut.Find(".schema-empty").TextContent).IsEqualTo("No jobs match.");
		await Assert.That(cut.FindAll("table").Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_markdown_timeline_body_renders_markdown_and_escapes_html()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","time":0,"body":"**bold** <script>alert(1)</script> [x](javascript:alert(2))"}]
			"""));

		var body = cut.Find(".schema-timeline-body");
		await Assert.That(body.InnerHtml).Contains("<strong>bold</strong>");
		await Assert.That(body.InnerHtml).DoesNotContain("<script>");
		await Assert.That(body.InnerHtml).Contains("&lt;script&gt;");
		await Assert.That(body.InnerHtml).DoesNotContain("javascript:");
	}

	[Test]
	public async Task A_code_timeline_body_is_verbatim_and_encoded()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","time":"0","format":"code","body":"**not bold** <b>x</b>\n  indented"}]
			"""));

		var code = cut.Find("pre.schema-timeline-code code");
		await Assert.That(code.TextContent).IsEqualTo("**not bold** <b>x</b>\n  indented");
		await Assert.That(code.InnerHtml).Contains("&lt;b&gt;");
		await Assert.That(cut.FindAll("strong").Count).IsEqualTo(0);
	}

	[Test]
	public async Task A_timeline_entry_shows_author_local_time_and_tag()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","time":1700000000,"body":"hi","tag":"Staff only","tag_color":"error"}]
			"""));

		var expected = DateTimeOffset.FromUnixTimeSeconds(1700000000).ToLocalTime()
			.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
		await Assert.That(cut.Find(".schema-timeline-author").TextContent).IsEqualTo("Ada");
		await Assert.That(cut.Find(".schema-timeline-time").TextContent).IsEqualTo(expected);
		var tag = cut.Find(".mud-chip");
		await Assert.That(tag.TextContent).Contains("Staff only");
		await Assert.That(tag.ClassName).Contains("error");
	}

	[Test]
	public async Task A_timeline_action_asks_its_renderer_to_dispatch_with_its_values()
	{
		SchemaActionRequest? requested = null;
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","time":0,"body":"hi","actions":[{"label":"Delete","action":"comment","values":{"delete":7}},
			                                                 {"label":"Ghost","action":"nowhere"}]}]
			"""), r => requested = r);

		var buttons = cut.FindAll(".schema-timeline-action");
		await Assert.That(buttons.Count).IsEqualTo(2);
		await Assert.That(buttons[1].HasAttribute("disabled")).IsTrue()
			.Because("an action the document does not declare cannot be dispatched");

		buttons[0].Click();

		await Assert.That(requested).IsNotNull();
		await Assert.That(requested!.Action).IsEqualTo("comment");
		await Assert.That(requested.Values!["delete"].GetInt32()).IsEqualTo(7);
	}

	[Test]
	public async Task A_button_with_confirm_dispatches_only_once_confirmed()
	{
		SchemaActionRequest? requested = null;
		var button = new SchemaElement(Kind: "button", Label: "Close job", Action: "close",
			Confirm: "Close this job?", Color: "error", Variant: "filled",
			Values: new Dictionary<string, JsonElement> { ["intent"] = JsonSerializer.SerializeToElement("close") });
		var cut = Render<DialogHarness>(p => p.AddChildContent<SchemaDisplay>(c => c
			.Add(x => x.Element, button)
			.Add(x => x.Actions, Actions)
			.Add(x => x.OnAction, (SchemaActionRequest r) => requested = r)));

		var rendered = cut.Find("button.schema-button");
		await Assert.That(rendered.ClassName).Contains("filled");
		await Assert.That(rendered.ClassName).Contains("error");

		rendered.Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Close this job?")) throw new InvalidOperationException("dialog not shown yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(requested).IsNull().Because("nothing is sent before the viewer confirms");

		cut.FindAll("button").First(b => b.TextContent.Contains("WidConfirm")).Click();
		cut.WaitForAssertion(() =>
		{
			if (requested is null) throw new InvalidOperationException("not dispatched yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(requested!.Action).IsEqualTo("close");
		await Assert.That(requested.Values!["intent"].GetString()).IsEqualTo("close");
	}

	[Test]
	public async Task Declining_a_confirmation_sends_nothing()
	{
		SchemaActionRequest? requested = null;
		var button = new SchemaElement(Kind: "button", Label: "Close job", Action: "close", Confirm: "Close this job?");
		var cut = Render<DialogHarness>(p => p.AddChildContent<SchemaDisplay>(c => c
			.Add(x => x.Element, button)
			.Add(x => x.Actions, Actions)
			.Add(x => x.OnAction, (SchemaActionRequest r) => requested = r)));

		cut.Find("button.schema-button").Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Close this job?")) throw new InvalidOperationException("dialog not shown yet");
		}, TimeSpan.FromSeconds(5));
		cut.FindAll("button").First(b => b.TextContent.Contains("Cancel")).Click();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("Close this job?")) throw new InvalidOperationException("dialog still open");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(requested).IsNull();
	}

	[Test]
	public async Task Unsafe_link_schemes_are_neutralised_and_safe_ones_kept()
	{
		await Assert.That(SchemaRendering.IsSafeUrl("/apps/jobs/1")).IsTrue();
		await Assert.That(SchemaRendering.IsSafeUrl("https://example.com/a:b")).IsTrue();
		await Assert.That(SchemaRendering.IsSafeUrl("jobs/1?x=a:b")).IsTrue();
		await Assert.That(SchemaRendering.IsSafeUrl("java\tscript:alert(1)")).IsFalse();
		await Assert.That(SchemaRendering.IsSafeUrl("data:text/html,x")).IsFalse();
	}
}
