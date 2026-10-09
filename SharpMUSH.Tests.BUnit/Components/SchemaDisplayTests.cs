using System.Text.Json;
using Bunit;
using MarkupString;
using MarkupString.Ansi;
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
	public async Task An_empty_chip_cell_draws_no_chip()
	{
		var cut = RenderDisplay(JobsTable(), Rows("jobs", """
			[{"id":"1","status":"","title":"a"},
			 {"id":"2","title":"b"}]
			"""));

		await Assert.That(cut.FindAll(".mud-chip").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(2);
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

		await buttons[0].ClickAsync();

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

		// The click waits on the confirmation this test answers below.
		await cut.StartClickAsync(rendered);
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Close this job?")) throw new InvalidOperationException("dialog not shown yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(requested).IsNull().Because("nothing is sent before the viewer confirms");

		await cut.FindAll("button").First(b => b.TextContent.Contains("WidConfirm")).ClickAsync();
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

		await cut.StartClickAsync(cut.Find("button.schema-button"));
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Close this job?")) throw new InvalidOperationException("dialog not shown yet");
		}, TimeSpan.FromSeconds(5));
		await cut.FindAll("button").First(b => b.TextContent.Contains("Cancel")).ClickAsync();
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

	[Test]
	public async Task Rows_sharing_a_group_draw_as_one_conversation_with_its_replies_indented()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Raya","body":"Route?","group":"1","anchor":"c1"},
			 {"author":"Grave","body":"Harbor.","group":"1","anchor":"c2","reply_to":"re [1] Raya"},
			 {"author":"Tomas","body":"Stairs closed.","group":"1","anchor":"c6","reply_to":"re [2] Grave","unread":true},
			 {"author":"Ann","body":"A stall.","group":"3","anchor":"c3"}]
			"""));

		var entries = cut.FindAll("li.schema-timeline-entry");
		await Assert.That(entries.Count).IsEqualTo(4);
		await Assert.That(entries[0].ClassList).DoesNotContain("schema-timeline-entry--reply");
		await Assert.That(entries[1].ClassList).Contains("schema-timeline-entry--reply");
		await Assert.That(entries[2].ClassList).Contains("schema-timeline-entry--reply");
		await Assert.That(entries[3].ClassList).Contains("schema-timeline-entry--starts")
			.Because("a new group starts a new conversation");
		await Assert.That(entries[3].ClassList).DoesNotContain("schema-timeline-entry--reply");

		await Assert.That(entries[2].Id).IsEqualTo("c6");
		await Assert.That(entries[2].QuerySelector(".schema-timeline-replyto")!.TextContent).IsEqualTo("re [2] Grave");
		await Assert.That(entries[2].ClassList).Contains("schema-timeline-entry--unread");
		await Assert.That(entries[2].QuerySelector(".schema-timeline-new")!.TextContent).IsEqualTo("WidTimelineNew");
		await Assert.That(cut.FindAll(".schema-timeline-new").Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_timeline_without_groups_draws_as_before()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","body":"one"},{"author":"Bo","body":"two"}]
			"""));

		foreach (var entry in cut.FindAll("li.schema-timeline-entry"))
		{
			await Assert.That(entry.ClassName).IsEqualTo("schema-timeline-entry");
			await Assert.That(entry.HasAttribute("id")).IsFalse();
		}
	}

	[Test]
	public async Task An_mstring_timeline_body_draws_its_markup_and_a_plain_one_is_encoded()
	{
		var styled = MarkupTextSerializer.Serialize(MarkupText.Concat(MarkupText.Plain("Ann's "),
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), "Teas")));
		var rows = JsonSerializer.Serialize(new object[]
		{
			new { author = "Ann", body = styled, format = "mstring" },
			new { author = "Bo", body = "<b>not a tag</b>", format = "mstring" },
		});
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", rows));

		var bodies = cut.FindAll(".schema-timeline-body.schema-markup");
		await Assert.That(bodies.Count).IsEqualTo(2);
		await Assert.That(bodies[0].TextContent).IsEqualTo("Ann's Teas");
		await Assert.That(bodies[0].InnerHtml).Contains("<span").Because("the colour is kept");
		await Assert.That(bodies[1].InnerHtml).Contains("&lt;b&gt;").And.DoesNotContain("<b>");
	}

	[Test]
	public async Task Hidden_replies_get_a_more_link_that_stays_in_the_portal()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","body":"deep","children_hidden":4,"more":"/apps/boards/5/3/5/2"},
			 {"author":"Bo","body":"evil","children_hidden":2,"more":"javascript:alert(1)"},
			 {"author":"Cy","body":"none hidden","more":"/apps/boards/5/3/5/9"}]
			"""));

		var more = cut.FindAll("a.schema-timeline-more");
		await Assert.That(more.Count).IsEqualTo(1);
		await Assert.That(more[0].GetAttribute("href")).IsEqualTo("/apps/boards/5/3/5/2");
	}

	[Test]
	public async Task An_entrys_links_are_followed_and_an_unsafe_one_is_dropped()
	{
		var cut = RenderDisplay(new SchemaElement(Kind: "timeline", RowsField: "entries"), Rows("entries", """
			[{"author":"Ada","body":"a","links":[{"label":"Reply","href":"/apps/boards/5/3/2"},
			  {"label":"Bad","href":"javascript:alert(1)"},{"label":"","href":"/apps/boards/1"}]},
			 {"author":"Bo","body":"b"}]
			"""));

		var links = cut.FindAll("a.schema-timeline-link");
		await Assert.That(links.Count).IsEqualTo(1);
		await Assert.That(links[0].TextContent).IsEqualTo("Reply");
		await Assert.That(links[0].GetAttribute("href")).IsEqualTo("/apps/boards/5/3/2");
		await Assert.That(cut.FindAll(".schema-timeline-actions").Count).IsEqualTo(1)
			.Because("an entry with neither links nor actions gets no action row");
	}

	[Test]
	public async Task A_fragment_naming_an_entry_scrolls_to_it_once()
	{
		Services.GetRequiredService<NavigationManager>().NavigateTo("/apps/boards/5/3/5#c2");
		var element = new SchemaElement(Kind: "timeline", RowsField: "entries");
		var data = Rows("entries", """[{"author":"Ada","body":"a","anchor":"c1"},{"author":"Bo","body":"b","anchor":"c2"}]""");
		var cut = RenderDisplay(element, data);
		cut.Render();

		var calls = JSInterop.Invocations.Where(i => i.Identifier == "SharpMUSH.scrollToId").ToList();
		await Assert.That(calls.Count).IsEqualTo(1).Because("a redraw does not pull the page back");
		await Assert.That(calls[0].Arguments[0]).IsEqualTo("c2");
	}

	[Test]
	public async Task An_mstring_markdown_element_draws_markup_not_markdown()
	{
		var styled = MarkupTextSerializer.Serialize(MarkupText.Plain("**plain** text"));
		var cut = RenderDisplay(new SchemaElement(Kind: "markdown", Value: styled, Format: "mstring"), null);

		var body = cut.Find(".schema-markup");
		await Assert.That(body.TextContent).IsEqualTo("**plain** text");
		await Assert.That(cut.FindAll("strong").Count).IsEqualTo(0);
	}

	private static SchemaElement PostsTable(SchemaReorder? reorder = null) => new(
		Kind: "table", RowsField: "posts", Reorder: reorder,
		Columns: [new SchemaColumn("num", "#"), new SchemaColumn("title", "Title")]);

	private const string GroupedPosts = """
		[{"num":"1","key":"p7","title":"Rules","group":"Pinned"},
		 {"num":"2","key":"p9","title":"Events","group":"Pinned"},
		 {"num":"3","key":"p3","title":"Hello","group":"Newest"},
		 {"num":"4","key":"p2","title":"Older","group":"Newest"}]
		""";

	[Test]
	public async Task A_table_draws_a_heading_row_where_its_row_group_changes()
	{
		var cut = RenderDisplay(PostsTable(), Rows("posts", GroupedPosts));

		var headings = cut.FindAll("tr.schema-table-group th");
		await Assert.That(headings.Select(h => h.TextContent).ToList()).IsEquivalentTo(new[] { "Pinned", "Newest" });
		await Assert.That(headings[0].GetAttribute("colspan")).IsEqualTo("2");
		await Assert.That(cut.FindAll("tbody tr").Count).IsEqualTo(6);
		await Assert.That(cut.FindAll(".schema-table-move").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Moving_a_row_in_the_reorderable_group_posts_its_key_and_new_place()
	{
		var requests = new List<SchemaActionRequest>();
		var reorder = new SchemaReorder("Pinned", "comment", "key",
			new Dictionary<string, JsonElement> { ["op"] = JsonSerializer.SerializeToElement("pin") });
		var cut = RenderDisplay(PostsTable(reorder), Rows("posts", GroupedPosts), requests.Add);

		var cells = cut.FindAll(".schema-table-move");
		await Assert.That(cells.Count).IsEqualTo(2).Because("only the Pinned rows can be moved");
		var firstButtons = cells[0].QuerySelectorAll("button");
		await Assert.That(firstButtons[0].HasAttribute("disabled")).IsTrue().Because("the first pin cannot go up");

		await cut.FindAll(".schema-table-move")[0].QuerySelectorAll("button")[1].ClickAsync();

		await Assert.That(requests.Count).IsEqualTo(1);
		await Assert.That(requests[0].Action).IsEqualTo("comment");
		await Assert.That(requests[0].Values!["op"].GetString()).IsEqualTo("pin");
		await Assert.That(requests[0].Values!["item"].GetString()).IsEqualTo("p7");
		await Assert.That(requests[0].Values!["position"].GetInt32()).IsEqualTo(2);
	}

	[Test]
	public async Task Dropping_a_dragged_row_on_another_in_its_group_moves_it_there()
	{
		var requests = new List<SchemaActionRequest>();
		var cut = RenderDisplay(PostsTable(new SchemaReorder("Pinned", "comment", "key")), Rows("posts", GroupedPosts), requests.Add);

		var rows = cut.FindAll("tr.schema-table-row--reorder");
		await rows[1].TriggerEventAsync("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
		await cut.FindAll("tr.schema-table-row--reorder")[0].TriggerEventAsync("ondrop", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

		await Assert.That(requests.Count).IsEqualTo(1);
		await Assert.That(requests[0].Values!["item"].GetString()).IsEqualTo("p9");
		await Assert.That(requests[0].Values!["position"].GetInt32()).IsEqualTo(1);
	}

	[Test]
	public async Task A_reorder_naming_an_action_the_document_lacks_offers_no_moves()
	{
		var cut = RenderDisplay(PostsTable(new SchemaReorder("Pinned", "nowhere", "key")), Rows("posts", GroupedPosts));

		await Assert.That(cut.FindAll(".schema-table-move").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("tr[draggable]").Count).IsEqualTo(0);
	}
}
