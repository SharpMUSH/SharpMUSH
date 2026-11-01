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
/// The editable <c>graph</c> field: the value it edits (<see cref="GraphDraft"/>), the guidance it reads
/// (<see cref="GraphGuidance"/>), and what the editor draws and sends.
/// </summary>
public class SchemaGraphEditorTests : BunitContext
{
	public SchemaGraphEditorTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PortsByKind = new Dictionary<string, IReadOnlyList<string>>
	{
		["challenge"] = ["success", "fail"],
		["narration"] = ["continue"],
		["end"] = []
	};

	private static IReadOnlyList<string> PortsOf(string kind) => PortsByKind.TryGetValue(kind, out var ports) ? ports : [];

	private const string Lantern = """
		{"title":"The Lantern Road","party":{"min":1,"max":4},"start":"gate",
		 "nodes":[
		   {"key":"gate","kind":"narration","label":"The gate","x":10,"y":10,"fields":{"text":"A lantern."}},
		   {"key":"bog","kind":"challenge","label":"Cross the bog","x":10,"y":150,"fields":{"options":[{"id":1,"trait":"Athletics"},{"id":2,"label":"Sneak round","trait":"Stealth"}]},"note":"kept"},
		   {"key":"mist","kind":"narration","label":"Lost in the mist","x":200,"y":150,"fields":{}},
		   {"key":"win","kind":"end","label":"Reward","x":10,"y":300,"fields":{}},
		   {"key":"toad","kind":"end","label":"Toad","x":200,"y":300,"fields":{}},
		   {"key":"bog","kind":"end","label":"Second bog"}],
		 "edges":[
		   {"from":"gate","port":"continue","to":"bog"},
		   {"from":"bog","port":"success","to":"win","weight":2},
		   {"from":"bog","port":"success","to":"mist","requires":"2"},
		   {"from":"bog","port":"fail","to":"toad","label":"kept"},
		   {"from":"mist","port":"continue","to":"bog"},
		   {"from":"bog","port":"fail","to":"nowhere"}]}
		""";

	private static readonly IReadOnlyList<SchemaElement> EdgeFields =
	[
		new(Kind: "field", Key: "weight", Label: "Weight", Type: "number", Default: Parse("1")),
		new(Kind: "field", Key: "requires", Label: "Needs", Type: "select", OptionsFrom: "options")
	];

	private static SchemaElement Field(bool readOnly = false, string? check = null) => new(
		Kind: "field", Key: "draft", Label: "Steps", Type: "graph",
		Graph: new SchemaGraphSpec(
			Kinds:
			[
				new SchemaGraphKind("challenge", "Challenge", "primary",
					Ports: [new SchemaGraphPort("success", "Success", "success", Many: true, EdgeFields: EdgeFields), new SchemaGraphPort("fail", "Fail", "error", Many: true)],
					Fields:
					[
						new SchemaElement(Kind: "field", Key: "text", Label: "What players read", Type: "textarea"),
						new SchemaElement(Kind: "field", Key: "options", Label: "Ways to beat it", Type: "rows",
							ItemFields: [new SchemaElement(Kind: "field", Key: "id", Type: "hidden"), new SchemaElement(Kind: "field", Key: "trait", Label: "Trait")])
					]),
				new SchemaGraphKind("narration", "Narration", Ports: [new SchemaGraphPort("continue", "Continue")]),
				new SchemaGraphKind("end", "End", "warning")
			],
			Check: check, IssuesField: "issues", NotesField: "notes", ChangesField: "changes", Readonly: readOnly));

	[Test]
	public async Task Reading_drops_a_repeated_step_and_an_edge_to_a_missing_one_and_keeps_everything_else()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		await Assert.That(draft.Nodes.Select(n => n.Key)).IsEquivalentTo(new[] { "gate", "bog", "mist", "win", "toad" });
		await Assert.That(draft.Edges.Count).IsEqualTo(5);

		var json = draft.ToJson();
		await Assert.That(json.GetProperty("title").GetString()).IsEqualTo("The Lantern Road");
		await Assert.That(json.GetProperty("party").GetProperty("max").GetInt32()).IsEqualTo(4);
		await Assert.That(json.GetProperty("start").GetString()).IsEqualTo("gate");
		var bog = json.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("key").GetString() == "bog");
		await Assert.That(bog.GetProperty("note").GetString()).IsEqualTo("kept");
		var fail = json.GetProperty("edges").EnumerateArray().Single(e => e.GetProperty("port").GetString() == "fail");
		await Assert.That(fail.GetProperty("label").GetString()).IsEqualTo("kept");
	}

	[Test]
	public async Task Anything_but_an_object_reads_as_an_empty_graph()
	{
		await Assert.That(GraphDraft.Read(null).Nodes.Count).IsEqualTo(0);
		await Assert.That(GraphDraft.Read(Parse("[1,2]")).Nodes.Count).IsEqualTo(0);
		await Assert.That(GraphDraft.Read(Parse("\"text\"")).Edges.Count).IsEqualTo(0);
	}

	[Test]
	public async Task An_outcome_may_lead_to_several_steps_each_edge_with_its_own_settings()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		await Assert.That(draft.Targets("bog", "success").Select(n => n.Key)).IsEquivalentTo(new[] { "win", "mist" });
		await Assert.That(draft.Edge("bog", "success", "win")?.Field("weight")?.GetDouble()).IsEqualTo(2d);

		draft.AddLink("bog", "success", "toad");
		draft.AddLink("bog", "success", "toad");
		draft.SetEdgeField("bog", "success", "toad", "min_successes", Parse("3"));
		draft.SetEdgeField("bog", "success", "win", "weight", null);
		draft.SetEdgeField("bog", "success", "win", "to", Parse("\"mist\""));
		draft.Unlink("bog", "success", "mist");

		var success = draft.ToJson().GetProperty("edges").EnumerateArray()
			.Where(e => e.GetProperty("port").GetString() == "success").ToList();
		await Assert.That(success.Select(e => e.GetProperty("to").GetString())).IsEquivalentTo(new[] { "win", "toad" });
		await Assert.That(success.Single(e => e.GetProperty("to").GetString() == "win").TryGetProperty("weight", out _)).IsFalse();
		await Assert.That(success.Single(e => e.GetProperty("to").GetString() == "toad").GetProperty("min_successes").GetInt32()).IsEqualTo(3);
	}

	[Test]
	public async Task Link_leaves_an_outcome_leading_to_one_step_or_none()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		draft.Link("bog", "success", "toad");
		await Assert.That(draft.Targets("bog", "success").Select(n => n.Key)).IsEquivalentTo(new[] { "toad" });

		draft.Link("bog", "success", null);
		await Assert.That(draft.Targets("bog", "success")).IsEmpty();

		draft.Link("bog", "success", "no-such-step");
		await Assert.That(draft.Targets("bog", "success")).IsEmpty();
	}

	[Test]
	public async Task Renaming_a_step_carries_its_edges_and_the_start_and_refuses_a_bad_or_taken_key()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		await Assert.That(draft.CheckKey("gate", "Gate!")).IsEqualTo(KeyProblem.Invalid);
		await Assert.That(draft.CheckKey("gate", "bog")).IsEqualTo(KeyProblem.Taken);
		await Assert.That(draft.Rename("gate", "bog")).IsFalse();

		await Assert.That(draft.Rename("gate", "lantern-gate")).IsTrue();
		await Assert.That(draft.Start).IsEqualTo("lantern-gate");
		await Assert.That(draft.Target("lantern-gate", "continue")?.Key).IsEqualTo("bog");

		await Assert.That(draft.Rename("bog", "bog_2")).IsTrue();
		await Assert.That(draft.Target("mist", "continue")?.Key).IsEqualTo("bog_2");
		await Assert.That(draft.Targets("bog_2", "success").Count).IsEqualTo(2);
	}

	[Test]
	public async Task Removing_a_step_drops_its_edges_and_moves_the_start_on()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		draft.Remove("gate");

		await Assert.That(draft.Edges.Any(e => e.From == "gate" || e.To == "gate")).IsFalse();
		await Assert.That(draft.StartNode?.Key).IsEqualTo("bog");
	}

	[Test]
	public async Task A_new_step_is_keyed_after_its_kind_and_the_first_is_where_a_run_begins()
	{
		var draft = GraphDraft.Read(null);

		var first = draft.Add("challenge", "Challenge");
		var second = draft.Add("challenge", "Challenge");
		var odd = draft.Add("Odd Kind!", "Odd");

		await Assert.That(first.Key).IsEqualTo("challenge1");
		await Assert.That(second.Key).IsEqualTo("challenge2");
		await Assert.That(odd.Key).IsEqualTo("oddkind1");
		await Assert.That(draft.Start).IsEqualTo("challenge1");
		await Assert.That(second.Y).IsGreaterThan(first.Y);
	}

	[Test]
	public async Task Reading_order_runs_from_the_start_through_each_outcome_in_turn_then_the_rest()
	{
		var draft = GraphDraft.Read(Parse(Lantern));
		draft.Unlink("mist", "continue", "bog");
		draft.Unlink("bog", "success", "mist");

		var order = draft.Order(PortsOf).Select(n => n.Key).ToList();

		await Assert.That(string.Join(" ", order)).IsEqualTo("gate bog win toad mist");
	}

	[Test]
	public async Task Tidy_puts_each_step_a_row_below_the_step_that_first_leads_to_it()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		draft.Tidy(PortsOf);
		var y = draft.Nodes.ToDictionary(n => n.Key, n => n.Y);
		var x = draft.Nodes.ToDictionary(n => n.Key, n => n.X);

		await Assert.That(y["bog"]).IsGreaterThan(y["gate"]);
		await Assert.That(y["win"]).IsEqualTo(y["mist"]);
		await Assert.That(y["toad"]).IsEqualTo(y["win"]);
		await Assert.That(new[] { x["win"], x["mist"], x["toad"] }.Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task Guidance_is_read_as_a_list_or_as_issues_and_hints()
	{
		var spec = new SchemaGraphSpec(IssuesField: "issues", NotesField: "notes", ChangesField: "changes");
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue>
		{
			["issues"] = new(Parse("""
				{"issues":[{"edge":"bog/fail","severity":"error","message":"Fail goes nowhere."},
				           {"node":"gate","message":"Short text."},{"node":"gate"}],
				 "hints":["Add a reward.",{"node":"win","message":"Rewards over 50 need staff."}]}
				""")),
			["notes"] = new(Parse("""[{"node":"win","author":"Ash","text":"Too generous."},{"node":"win"}]""")),
			["changes"] = new(Parse("""[{"node":"bog","change":"Added"},{"node":"mist","change":"moved"}]"""))
		});

		var guidance = GraphGuidance.Read(spec, data);

		await Assert.That(guidance.Issues.Count).IsEqualTo(4);
		var fail = guidance.Issues[0];
		await Assert.That((fail.Node, fail.Port, fail.Severity)).IsEqualTo(("bog", "fail", "error"));
		await Assert.That(guidance.Issues[1].Severity).IsEqualTo("warning");
		await Assert.That(guidance.Issues.Count(i => i.Severity == "hint")).IsEqualTo(2);
		await Assert.That(guidance.NotesOn("win").Single().Author).IsEqualTo("Ash");
		await Assert.That(guidance.Changes.Single().Node).IsEqualTo("bog");
	}

	[Test]
	public async Task The_outline_lists_each_outcome_with_where_it_leads()
	{
		var cut = Render<SchemaGraphEditor>(p => p.Add(x => x.Element, Field()).Add(x => x.Value, Parse(Lantern)));

		var bog = cut.FindAll(".graph-outline-step").Single(li => li.QuerySelector(".graph-outline-label")?.TextContent == "Cross the bog");
		await Assert.That(bog.TextContent).Contains("WidGraphOneOf(Reward (Weight: 2), Lost in the mist (Needs: Sneak round))");
		await Assert.That(bog.TextContent).Contains("Toad");
		var win = cut.FindAll(".graph-outline-step").Single(li => li.QuerySelector(".graph-outline-label")?.TextContent == "Reward");
		await Assert.That(win.QuerySelectorAll(".graph-outline-ports").Length).IsEqualTo(0);
	}

	[Test]
	public async Task Adding_a_step_after_the_selected_one_links_its_first_open_outcome()
	{
		JsonElement? sent = null;
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, Field())
			.Add(x => x.Value, Parse("""{"nodes":[{"key":"bog","kind":"challenge","label":"Cross the bog","x":0,"y":0}],"edges":[]}"""))
			.Add(x => x.ValueChanged, (JsonElement v) => sent = v));

		await cut.Find(".graph-node").ClickAsync(new());
		await cut.FindAll(".graph-add button").Single(b => b.TextContent.Contains("End")).ClickAsync(new());

		var draft = GraphDraft.Read(sent);
		await Assert.That(draft.Nodes.Select(n => n.Key)).IsEquivalentTo(new[] { "bog", "end1" });
		await Assert.That(draft.Target("bog", "success")?.Key).IsEqualTo("end1");
		await Assert.That(draft.StartNode?.Key).IsEqualTo("bog");
	}

	private static SchemaElement RequiredField() => new(
		Kind: "field", Key: "draft", Label: "Steps", Type: "graph",
		Graph: new SchemaGraphSpec(Kinds:
		[
			new SchemaGraphKind("challenge", "Challenge",
				Ports: [new SchemaGraphPort("crit", "Critical"), new SchemaGraphPort("success", "Success", Required: true)]),
			new SchemaGraphKind("end", "End")
		]));

	[Test]
	public async Task Adding_a_step_follows_an_outcome_the_selected_step_must_have_and_stands_clear_of_the_others()
	{
		JsonElement? sent = null;
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, RequiredField())
			.Add(x => x.Value, Parse("""{"nodes":[{"key":"bog","kind":"challenge","label":"Bog","x":0,"y":0},{"key":"won","kind":"end","label":"Won","x":0,"y":132}],"edges":[]}"""))
			.Add(x => x.ValueChanged, (JsonElement v) => sent = v));

		// Both outcomes lead nowhere: the one the step must have is drawn as a stub, the other is not.
		await Assert.That(cut.FindAll(".graph-stub").Count).IsEqualTo(1);
		await cut.FindAll(".graph-node").Single(b => b.GetAttribute("data-key") == "bog").ClickAsync(new());
		await cut.FindAll(".graph-add button").Single(b => b.TextContent.Contains("End")).ClickAsync(new());

		var draft = GraphDraft.Read(sent);
		await Assert.That(draft.Target("bog", "success")?.Key).IsEqualTo("end1");
		await Assert.That(draft.Target("bog", "crit")).IsNull();
		var added = draft.Find("end1")!;
		await Assert.That(Math.Abs(added.X - 0) < GraphDraft.NodeWidth && Math.Abs(added.Y - 132) < GraphDraft.NodeHeight).IsFalse();
	}

	[Test]
	public async Task Dragging_an_outcome_handle_onto_a_step_links_it()
	{
		JsonElement? sent = null;
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, Field())
			.Add(x => x.Value, Parse(Lantern))
			.Add(x => x.ValueChanged, (JsonElement v) => sent = v));

		await cut.FindAll(".graph-node").Single(b => b.GetAttribute("data-key") == "bog").ClickAsync(new());
		// The drag starts under bog (10, 150) at (96, 218) and ends inside mist (200, 150).
		await cut.FindAll(".graph-handle").Single(b => b.TextContent == "Fail").PointerDownAsync(new() { Button = 0, ClientX = 0, ClientY = 0 });
		await cut.Find(".graph-map").PointerMoveAsync(new() { ClientX = 190, ClientY = -34 });
		await Assert.That(cut.FindAll(".graph-line--pending").Count).IsEqualTo(1);
		await cut.Find(".graph-map").PointerUpAsync(new());

		var draft = GraphDraft.Read(sent);
		await Assert.That(draft.Targets("bog", "fail").Select(n => n.Key)).IsEquivalentTo(new[] { "toad", "mist" });
		await Assert.That(cut.FindAll(".graph-line--pending").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Dragging_the_start_markers_handle_onto_a_step_makes_the_run_begin_there()
	{
		JsonElement? sent = null;
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, Field())
			.Add(x => x.Value, Parse(Lantern))
			.Add(x => x.ValueChanged, (JsonElement v) => sent = v));

		await Assert.That(cut.FindAll(".graph-line--entry").Count).IsEqualTo(1);
		await cut.Find(".graph-entry").ClickAsync(new());
		// The marker stands above gate (10, 10); its handle starts the line at (96, -30), and the drag ends inside mist.
		await cut.Find(".graph-handle").PointerDownAsync(new() { Button = 0, ClientX = 0, ClientY = 0 });
		await cut.Find(".graph-map").PointerMoveAsync(new() { ClientX = 200, ClientY = 200 });
		await cut.Find(".graph-map").PointerUpAsync(new());

		await Assert.That(GraphDraft.Read(sent).StartNode?.Key).IsEqualTo("mist");
	}

	[Test]
	public async Task The_start_marker_stands_above_the_first_step_until_moved_and_tidying_puts_it_back()
	{
		var draft = GraphDraft.Read(Parse(Lantern));

		await Assert.That(draft.Entry).IsEqualTo((48d, -66d));
		draft.MoveEntry(300.4, 20.6);
		await Assert.That(draft.Entry).IsEqualTo((300d, 21d));
		var moved = GraphDraft.Read(draft.ToJson());
		await Assert.That(moved.Entry).IsEqualTo((300d, 21d));

		moved.Tidy(PortsOf);
		await Assert.That(moved.ToJson().TryGetProperty("start_x", out _)).IsFalse();
		await Assert.That(moved.Entry.Y).IsEqualTo(moved.StartNode?.Y - GraphDraft.EntryHeight - 40);
	}

	[Test]
	public async Task A_spot_for_a_step_is_found_beside_the_ones_in_the_way()
	{
		var draft = GraphDraft.Read(Parse("""{"nodes":[{"key":"a","kind":"end","label":"A","x":0,"y":0},{"key":"b","kind":"end","label":"B","x":244,"y":0}],"edges":[]}"""));

		await Assert.That(draft.At(10, 10)?.Key).IsEqualTo("a");
		await Assert.That(draft.At(230, 10)).IsNull();
		await Assert.That(draft.FreeSpot(0, 0)).IsEqualTo((488d, 0d));
		await Assert.That(draft.FreeSpot(0, 0, except: "a")).IsEqualTo((0d, 0d));
		await Assert.That(draft.FreeSpot(0, 400)).IsEqualTo((0d, 400d));
	}

	[Test]
	public async Task A_step_shows_the_fields_its_kind_declares_and_an_edit_lands_in_the_step()
	{
		JsonElement? sent = null;
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, Field())
			.Add(x => x.Value, Parse(Lantern))
			.Add(x => x.ValueChanged, (JsonElement v) => sent = v));

		await cut.FindAll(".graph-node").Single(b => b.GetAttribute("data-key") == "bog").ClickAsync(new());
		var text = cut.Find(".graph-inspector textarea");
		await text.ChangeAsync(new() { Value = "Black water." });

		var bog = GraphDraft.Read(sent).Find("bog")!;
		await Assert.That(bog.Field("text")?.GetString()).IsEqualTo("Black water.");
		await Assert.That(cut.Find(".graph-inspector").TextContent).Contains("WidGraphLeadsTo(Success)");
	}

	[Test]
	public async Task A_read_only_graph_offers_no_way_to_change_it()
	{
		var cut = Render<SchemaGraphEditor>(p => p.Add(x => x.Element, Field(readOnly: true)).Add(x => x.Value, Parse(Lantern)));

		await Assert.That(cut.FindAll(".graph-add").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".graph-node--movable").Count).IsEqualTo(0);
		await cut.FindAll(".graph-node").Single(b => b.GetAttribute("data-key") == "bog").ClickAsync(new());
		await Assert.That(cut.Find(".graph-inspector-fields").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task Check_dispatches_the_named_action_and_its_guidance_marks_the_step()
	{
		SchemaActionRequest? asked = null;
		var data = new SchemaData(new Dictionary<string, SchemaFieldValue>
		{
			["issues"] = new(Parse("""[{"edge":"gate/continue","severity":"warning","message":"Gate needs more text."}]"""))
		});
		var cut = Render<SchemaGraphEditor>(p => p
			.Add(x => x.Element, Field(check: "check"))
			.Add(x => x.Value, Parse(Lantern))
			.Add(x => x.Data, data)
			.Add(x => x.OnAction, (SchemaActionRequest r) => asked = r));

		await cut.FindAll("button").Single(b => b.TextContent.Contains("WidGraphCheck")).ClickAsync(new());

		await Assert.That(asked?.Action).IsEqualTo("check");
		var gate = cut.FindAll(".graph-node").Single(b => b.GetAttribute("data-key") == "gate");
		await Assert.That(gate.QuerySelector(".graph-badge--issue")?.TextContent).IsEqualTo("1");
		await cut.Find(".graph-guidance .graph-link").ClickAsync(new());
		await Assert.That(cut.Find(".graph-node--selected").GetAttribute("data-key")).IsEqualTo("gate");
	}

	[Test]
	public async Task A_rows_field_adds_a_row_from_its_item_defaults()
	{
		object? sent = null;
		var rows = new SchemaElement(Kind: "field", Key: "options", Label: "Ways to beat it", Type: "rows",
			ItemFields:
			[
				new SchemaElement(Kind: "field", Key: "trait", Label: "Trait", Type: "text"),
				new SchemaElement(Kind: "field", Key: "successes", Label: "Successes", Type: "number", Default: Parse("1"))
			]);
		var cut = Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, rows)
			.Add(x => x.Value, Parse("""[{"trait":"Athletics","successes":2}]"""))
			.Add(x => x.ValueChanged, (object? v) => sent = v));

		await Assert.That(cut.FindAll(".schema-row").Count).IsEqualTo(1);
		await cut.FindAll("button").Single(b => b.TextContent.Contains("WidRowsAdd")).ClickAsync(new());

		var array = (JsonElement)sent!;
		await Assert.That(array.GetArrayLength()).IsEqualTo(2);
		await Assert.That(array[0].GetProperty("trait").GetString()).IsEqualTo("Athletics");
		await Assert.That(array[1].GetProperty("successes").GetInt32()).IsEqualTo(1);
		await Assert.That(array[1].TryGetProperty("id", out _)).IsFalse();
	}

	[Test]
	public async Task A_new_row_takes_the_next_id_when_its_rows_carry_one()
	{
		object? sent = null;
		var rows = new SchemaElement(Kind: "field", Key: "options", Type: "rows",
			ItemFields: [new SchemaElement(Kind: "field", Key: "id", Type: "hidden"), new SchemaElement(Kind: "field", Key: "trait", Label: "Trait")]);
		var cut = Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, rows)
			.Add(x => x.Value, Parse("""[{"id":1,"trait":"Athletics"},{"id":"4","trait":"Stealth"}]"""))
			.Add(x => x.ValueChanged, (object? v) => sent = v));

		await cut.FindAll("button").Single(b => b.TextContent.Contains("WidRowsAdd")).ClickAsync(new());

		await Assert.That(((JsonElement)sent!)[2].GetProperty("id").GetInt32()).IsEqualTo(5);
	}

	[Test]
	public async Task A_row_that_changes_kind_takes_that_kinds_details()
	{
		object? sent = null;
		var rows = new SchemaElement(Kind: "field", Key: "rewards", Type: "rows",
			ItemFields: [new SchemaElement(Kind: "field", Key: "kind", Label: "Kind")],
			ItemBy: "kind",
			ItemVariants: new Dictionary<string, IReadOnlyList<SchemaElement>>
			{
				["info"] = [new SchemaElement(Kind: "field", Key: "text", Label: "Info text")],
				["coin"] = [new SchemaElement(Kind: "field", Key: "amount", Label: "Amount", Type: "number", Default: Parse("10"))]
			});
		var cut = Render<SchemaFieldInput>(p => p
			.Add(x => x.Element, rows)
			.Add(x => x.Value, Parse("""[{"kind":"info","text":"A lantern","note":"kept"}]"""))
			.Add(x => x.ValueChanged, (object? v) => sent = v));

		await Assert.That(cut.Markup).Contains("Info text");
		await Assert.That(cut.Markup).DoesNotContain("Amount");

		await cut.FindAll("input").First().ChangeAsync(new() { Value = "coin" });

		var row = ((JsonElement)sent!)[0];
		await Assert.That(row.GetProperty("kind").GetString()).IsEqualTo("coin");
		await Assert.That(row.GetProperty("amount").GetInt32()).IsEqualTo(10);
		await Assert.That(row.TryGetProperty("text", out _)).IsFalse();
		await Assert.That(row.GetProperty("note").GetString()).IsEqualTo("kept");
	}
}
