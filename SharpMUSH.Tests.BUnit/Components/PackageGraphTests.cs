using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages.Admin.Packages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;
using G = SharpMUSH.Client.Models.PackageDependencyGraph;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>The package dependency graph: where its packages go and what selecting one traces.</summary>
public class PackageGraphTests : BunitContext
{
	public PackageGraphTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static G.Node Node(string id, PackageGraphState state = PackageGraphState.Available) => new(id, id, state);

	// dice <- sheet <- sheet-chargen -> chargen; tools stands alone.
	private static readonly IReadOnlyList<G.Node> Nodes =
		[Node("sheet-chargen"), Node("sheet"), Node("dice", PackageGraphState.Installed), Node("chargen"), Node("tools")];

	private static readonly IReadOnlyList<G.Edge> Edges =
		[new("sheet", "dice"), new("sheet-chargen", "sheet"), new("sheet-chargen", "chargen"), new("sheet", "nowhere"), new("sheet", "sheet")];

	[Test]
	public async Task EachPackageSitsOneColumnPastTheFurthestPackageItNeeds()
	{
		var drawing = G.Place(Nodes, Edges);
		var columns = drawing.Nodes.ToDictionary(n => n.Node.Id, n => n.Column);

		await Assert.That(columns["dice"]).IsEqualTo(0);
		await Assert.That(columns["chargen"]).IsEqualTo(0);
		await Assert.That(columns["sheet"]).IsEqualTo(1);
		await Assert.That(columns["sheet-chargen"]).IsEqualTo(2);
		await Assert.That(drawing.Edges.Count).IsEqualTo(3).Because("an edge to a node not given, or to itself, is left out");
		await Assert.That(drawing.Unjoined.Select(n => n.Id)).IsEquivalentTo(["tools"]);
		await Assert.That(drawing.Width).IsEqualTo(3 * G.NodeWidth + 2 * G.ColumnGap);
	}

	[Test]
	public async Task EveryLineRunsFromThePackageThatNeedsBackToWhatItNeeds()
	{
		var drawing = G.Place(Nodes, Edges);
		var placed = drawing.Nodes.ToDictionary(n => n.Node.Id);

		foreach (var edge in drawing.Edges)
		{
			await Assert.That(edge.X1).IsEqualTo(placed[edge.Edge.Dependent].X);
			await Assert.That(edge.X2).IsEqualTo(placed[edge.Edge.Dependency].X + G.NodeWidth);
			await Assert.That(edge.X1).IsGreaterThan(edge.X2);
		}
	}

	[Test]
	public async Task ACycleIsBrokenAndEveryPackageStillGetsAColumn()
	{
		var drawing = G.Place([Node("a"), Node("b"), Node("c")], [new("a", "b"), new("b", "c"), new("c", "a")]);

		await Assert.That(drawing.Nodes.Count).IsEqualTo(3);
		await Assert.That(drawing.Nodes.Select(n => n.Column).Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task NoPackagesOverlap_InALargeGraph()
	{
		var nodes = Enumerable.Range(0, 200).Select(i => Node($"p{i}")).ToList();
		var edges = Enumerable.Range(1, 199).SelectMany(i => new G.Edge[] { new($"p{i}", $"p{i / 3}"), new($"p{i}", $"p{i * 7 % 200}") }).ToList();
		var drawing = G.Place(nodes, edges);

		var gaps = drawing.Nodes.GroupBy(n => n.Column)
			.SelectMany(c => c.Select(n => n.Y).Order().Zip(c.Select(n => n.Y).Order().Skip(1), (a, b) => b - a));
		await Assert.That(gaps.Min()).IsGreaterThanOrEqualTo(G.NodeHeight + G.RowGap - 0.001);
		await Assert.That(drawing.Nodes.Min(n => n.Y)).IsEqualTo(0);
		await Assert.That(drawing.Nodes.Max(n => n.Y + G.NodeHeight)).IsLessThanOrEqualTo(drawing.Height + 0.001);
	}

	[Test]
	public async Task APackageNeededByManySitsAmongThem()
	{
		var nodes = Enumerable.Range(0, 9).Select(i => Node($"b{i}")).Append(Node("hub")).Append(Node("base")).ToList();
		var edges = Enumerable.Range(0, 9).Select(i => new G.Edge($"b{i}", "hub")).Append(new G.Edge("b0", "base")).ToList();
		var drawing = G.Place(nodes, edges);
		var y = drawing.Nodes.ToDictionary(n => n.Node.Id, n => n.Y);
		var dependents = Enumerable.Range(0, 9).Select(i => y[$"b{i}"]).ToList();

		await Assert.That(y["hub"]).IsBetween(dependents.Min(), dependents.Max());
		await Assert.That(y["hub"]).IsGreaterThan(y["base"]);
		await Assert.That(Math.Abs(y["base"] - y["b0"])).IsLessThan(Math.Abs(y["hub"] - y["b0"]))
			.Because("a package needed by one sits beside it");
	}

	[Test]
	public async Task AFilteredGraphKeepsWhatItsPackagesNeed()
	{
		var shown = G.WithDependencies(["sheet-chargen"], Edges);

		await Assert.That(shown.Order()).IsEquivalentTo(new[] { "chargen", "dice", "nowhere", "sheet", "sheet-chargen" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task SelectingAPackageTracesWhatItNeedsAndWhatNeedsIt_AndDimsTheRest()
	{
		var cut = Render<PackageGraph>(p => p.Add(x => x.Nodes, Nodes).Add(x => x.Edges, Edges));
		await cut.Find(".pkg-graph-node[aria-label^='PkgGraphNodeLabel(sheet,']").ClickAsync();

		await Assert.That(cut.Find(".pkg-graph-node--selected").TextContent).Contains("sheet");
		await Assert.That(cut.FindAll(".pkg-graph-node--traced").Select(n => n.QuerySelector(".pkg-graph-id")!.TextContent))
			.IsEquivalentTo(["dice", "sheet-chargen"]);
		await Assert.That(cut.FindAll(".pkg-graph-node--dimmed").Select(n => n.QuerySelector(".pkg-graph-id")!.TextContent))
			.IsEquivalentTo(["chargen"]);
		await Assert.That(cut.FindAll(".pkg-graph-edge--traced").Count).IsEqualTo(2);
		var relations = cut.FindAll(".pkg-graph-relation");
		await Assert.That(relations[0].TextContent).Contains("PkgGraphNeeds").And.Contains("dice");
		await Assert.That(relations[1].TextContent).Contains("PkgGraphNeededBy").And.Contains("sheet-chargen");
		await Assert.That(cut.Find(".pkg-graph-unjoined").TextContent).Contains("tools");

		await cut.Find(".pkg-graph-node--selected").ClickAsync();
		await Assert.That(cut.FindAll(".pkg-graph-node--dimmed")).IsEmpty().Because("selecting the package again clears the trace");
	}

	[Test]
	public async Task ANodeTellsAScreenReaderWhatItNeedsAndWhatNeedsIt()
	{
		var cut = Render<PackageGraph>(p => p.Add(x => x.Nodes, Nodes).Add(x => x.Edges, Edges));

		await Assert.That(cut.Find(".pkg-graph-node[aria-label^='PkgGraphNodeLabel(sheet,']").GetAttribute("aria-label"))
			.IsEqualTo("PkgGraphNodeLabel(sheet, PkgGraphAvailable, dice, sheet-chargen)");
	}
}
