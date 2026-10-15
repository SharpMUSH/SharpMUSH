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
/// The schema <c>graph</c> element: where its nodes go (<see cref="SchemaGraphLayout"/>), what it draws for
/// them, and the keyvalue values that arrive as <c>{ text, link }</c>.
/// </summary>
public class SchemaGraphTests : BunitContext
{
	public SchemaGraphTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private const string Web = """
		{"nodes":[{"id":"#1:1","label":"Wren","focus":true},{"id":"#2:1","label":"Ash"},{"id":"#3:1","label":"Rook"},
		          {"id":"#4:1","label":"Moth"},{"id":"#2:1","label":"Ash again"},{"label":"No id"}],
		 "edges":[{"from":"#2:1","to":"#1:1","label":"Sibling","tone":"purple","directed":false},
		          {"from":"#3:1","to":"#1:1","label":"Parent","tone":"no-such-tone","directed":true},
		          {"from":"#4:1","to":"#2:1","label":"Friend","tone":"green"},
		          {"from":"#9:1","to":"#1:1","label":"Nobody"}]}
		""";

	private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static SchemaGraphLayout.Drawing Draw(string json)
	{
		var (nodes, edges) = SchemaGraphLayout.Read(Parse(json));
		return SchemaGraphLayout.Place(nodes, edges);
	}

	private static double Distance(SchemaGraphLayout.PlacedNode a, SchemaGraphLayout.PlacedNode b)
		=> Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

	[Test]
	public async Task A_repeated_id_a_node_without_one_and_an_edge_to_a_missing_node_are_left_out()
	{
		var (nodes, edges) = SchemaGraphLayout.Read(Parse(Web));

		await Assert.That(nodes.Select(n => n.Label)).IsEquivalentTo(new[] { "Wren", "Ash", "Rook", "Moth" });
		await Assert.That(edges.Select(e => e.Label)).IsEquivalentTo(new[] { "Sibling", "Parent", "Friend" });
	}

	[Test]
	public async Task The_focus_sits_in_the_middle_and_each_ring_one_step_further_out()
	{
		var drawing = Draw(Web);
		var at = drawing.Nodes.ToDictionary(n => n.Node.Label);

		await Assert.That(at["Wren"].Ring).IsEqualTo(0);
		await Assert.That(at["Wren"].X).IsEqualTo(drawing.Centre);
		await Assert.That(at["Wren"].Y).IsEqualTo(drawing.Centre);
		await Assert.That(at["Ash"].Ring).IsEqualTo(1);
		await Assert.That(at["Rook"].Ring).IsEqualTo(1);
		await Assert.That(at["Moth"].Ring).IsEqualTo(2);
		await Assert.That(Distance(at["Wren"], at["Ash"])).IsEqualTo(SchemaGraphLayout.RingStep).Within(0.001);
		await Assert.That(Distance(at["Wren"], at["Moth"])).IsEqualTo(2 * SchemaGraphLayout.RingStep).Within(0.001);
	}

	/// <summary>Moth is reached through Ash, so it hangs outward from Ash rather than across the drawing.</summary>
	[Test]
	public async Task An_outer_node_sits_beyond_the_inner_node_that_reaches_it()
	{
		var at = Draw(Web).Nodes.ToDictionary(n => n.Node.Label);

		await Assert.That(Distance(at["Ash"], at["Moth"])).IsLessThan(Distance(at["Rook"], at["Moth"]));
		await Assert.That(Distance(at["Ash"], at["Moth"])).IsEqualTo(SchemaGraphLayout.RingStep).Within(0.001);
	}

	[Test]
	public async Task A_node_no_edge_reaches_still_gets_a_place()
	{
		var at = Draw("""{"nodes":[{"id":"a","label":"A","focus":true},{"id":"b","label":"B"}],"edges":[]}""").Nodes.ToDictionary(n => n.Node.Label);

		await Assert.That(at["B"].Ring).IsEqualTo(1);
	}

	private IRenderedComponent<SchemaDisplay> RenderGraph(string json, string? empty = null) =>
		Render<SchemaDisplay>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "graph", SrcField: "web", Link: "/character/{label}", Empty: empty))
			.Add(x => x.Data, new SchemaData(new Dictionary<string, SchemaFieldValue> { ["web"] = new(Parse(json)) })));

	[Test]
	public async Task It_draws_a_line_per_edge_and_an_arrow_only_on_a_directed_one()
	{
		var cut = RenderGraph(Web);

		await Assert.That(cut.FindAll("line.schema-graph-edge").Count).IsEqualTo(3);
		await Assert.That(cut.FindAll("line.schema-graph-edge[marker-end]").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("text.schema-graph-edge-label").Select(t => t.TextContent)).IsEquivalentTo(new[] { "Sibling", "Parent", "Friend" });
	}

	[Test]
	public async Task A_tone_colours_its_line_and_an_unknown_tone_takes_the_theme_line()
	{
		var cut = RenderGraph(Web);
		var lines = cut.FindAll("line.schema-graph-edge");

		await Assert.That(lines[0].GetAttribute("style")).IsEqualTo("stroke: var(--hue-purple);");
		await Assert.That(lines[1].HasAttribute("style")).IsFalse();
	}

	[Test]
	public async Task Each_node_links_through_the_template_filled_from_it()
	{
		var cut = RenderGraph(Web);

		var links = cut.FindAll("a.schema-graph-hit");
		await Assert.That(links.Select(a => a.GetAttribute("href"))).IsEquivalentTo(new[] { "/character/Wren", "/character/Ash", "/character/Rook", "/character/Moth" });
		await Assert.That(links.Select(a => a.GetAttribute("aria-label"))).Contains("Ash");
	}

	[Test]
	public async Task A_reader_gets_the_edges_as_a_list()
	{
		var cut = RenderGraph(Web);

		await Assert.That(cut.Find("svg").GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(cut.FindAll("figcaption li").Select(li => li.TextContent)).Contains("WidGraphEdge(Rook, Wren, Parent)");
	}

	[Test]
	public async Task Without_edges_it_shows_its_empty_text()
	{
		var cut = RenderGraph("""{"nodes":[{"id":"a","label":"A","focus":true}],"edges":[]}""", empty: "No relationships yet.");

		await Assert.That(cut.FindAll("svg").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".schema-empty").TextContent).IsEqualTo("No relationships yet.");
	}

	private IRenderedComponent<SchemaDisplay> RenderKeyValue(string json) =>
		Render<SchemaDisplay>(p => p
			.Add(x => x.Element, new SchemaElement(Kind: "keyvalue", Fields: ["Last scene"]))
			.Add(x => x.Data, new SchemaData(new Dictionary<string, SchemaFieldValue> { ["Last scene"] = new(Parse(json)) })));

	[Test]
	public async Task A_keyvalue_given_text_and_a_link_shows_the_text_as_that_link()
	{
		var cut = RenderKeyValue("""{"text":"The fire","link":"/scenes/1"}""");

		var link = cut.Find("dd a.schema-link");
		await Assert.That(link.GetAttribute("href")).IsEqualTo("/scenes/1");
		await Assert.That(link.TextContent).IsEqualTo("The fire");
	}

	[Test]
	public async Task A_keyvalue_link_the_portal_may_not_follow_is_plain_text()
	{
		var cut = RenderKeyValue("""{"text":"The fire","link":"javascript:alert(1)"}""");

		await Assert.That(cut.FindAll("dd a").Count).IsEqualTo(0);
		await Assert.That(cut.Find("dd").TextContent).IsEqualTo("The fire");
	}
}
