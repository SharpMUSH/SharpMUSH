using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// graph_reach(), graph_path(), graph_fold() and json_walk() over the four example graphs of the design: a skill
/// tree with a shared prerequisite, a faction tree, a travel graph with costs and quest dependencies.
/// </summary>
public class GraphFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string[] LabAttributes =
	[
		"SKILL`WHIRL`NEEDS=DUAL RIPOSTE",
		"SKILL`RIPOSTE`NEEDS=PARRY",
		"SKILL`PARRY`NEEDS=SWORDS",
		"SKILL`DUAL`NEEDS=SWORDS",
		"SKILL`SWORDS`COST=1",
		"SKILL`PARRY`COST=2",
		"SKILL`DUAL`COST=2",
		"SKILL`RIPOSTE`COST=3",
		"SKILL`WHIRL`COST=5",
		"FACTION`CROWN`SUBS=GUARD COURT",
		"FACTION`GUARD`SUBS=NIGHTWATCH",
		"FACTION`CROWN`MEMBERS=Aldo",
		"FACTION`GUARD`MEMBERS=Bea Cyr",
		"FACTION`NIGHTWATCH`MEMBERS=Dag Eve Fen",
		"FACTION`COURT`MEMBERS=Gil",
		"WAY`HARBOR`TO`DOCKS`COST=1",
		"WAY`HARBOR`TO`MARKET`COST=4",
		"WAY`DOCKS`TO`MARKET`COST=1",
		"WAY`DOCKS`TO`FORT`COST=5",
		"WAY`MARKET`TO`FORT`COST=1",
		"QUEST`VAULT`NEEDS=LANTERN KEY",
		"QUEST`LANTERN`NEEDS=FIRSTSTEPS",
		"QUEST`KEY`NEEDS=FIRSTSTEPS",
		"FUN`NEEDS=get(%!/SKILL`%0`NEEDS)",
		"FUN`SUBS=get(%!/FACTION`%0`SUBS)",
		"FUN`WAYS=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))",
		"FUN`COST=get(%!/WAY`%0`TO`%1`COST)",
		"FUN`QNEEDS=if(strmatch(%0,%q<editing>),%q<proposed>,get(%!/QUEST`%0`NEEDS))",
		"FUN`DIAMOND=switch(%0,A,B|C,B,D,C,D)",
		"FUN`SELF=switch(%0,A,A)",
		"FUN`INDENT=[space(mul(2,%1))]%0",
		"FUN`COUNT=add(words(get(%!/FACTION`%0`MEMBERS)),ladd(%1,|))",
		"FUN`DRAW=lnode(%0 %(%2%),%1,|)",
		"FUN`RANK=if(%1,inc(%1),1)",
		"FUN`SHOWRANK=%0=%2",
		"FUN`WRAP=%0<%1>",
		"FUN`WHERE=%1/%2/%3",
		"FUN`VISIT=%0:%1:%2",
		"FUN`LEAF=if(%1,json_query(json_query(%4,extract,%3),get,x))",
		"FUN`TITLE=json_query(json_query(%0,get,title),unescape)",
		"FUN`LINE=[space(mul(2,%1))][u(%!/FUN`TITLE,%0)]",
		"FUN`COSTOF=json_query(%0,get,cost)",
		"FUN`BACK=json_query(%4,extract,%3)",
		"FUN`EXTRA=%4-%5",
		"DATA`API={\"title\":\"Swords\",\"cost\":1,\"subskills\":[{\"title\":\"Parry\",\"cost\":2,\"subskills\":[{\"title\":\"Riposte\",\"cost\":3}]},{\"title\":\"Dual\",\"cost\":2}]}",
		"DATA`ROOTS=[{\"children\":[{}]},{}]",
		"DATA`DEEP={\"links\":{\"items\":[{\"a\":1},{\"b\":2,\"links\":{\"items\":[{\"c\":3}]}}]}}",
		"DATA`OBJ={\"kids\":{\"9x\":{},\"ok\":{\"kids\":{\"it's\":{}}}}}",
		"DATA`CTRL={\"kids\":{\"a\\nb\":{\"x\":1}}}"
	];

	/// <summary>A fresh object holding the example graphs and functions; returns its dbref.</summary>
	private async Task<string> CreateLab()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("GraphLab");
		var created = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {name}"));
		var dbref = $"#{DBRef.Parse(created.Message.ToPlainText()!).Number}";
		foreach (var attribute in LabAttributes)
		{
			var split = attribute.IndexOf('=');
			await CommandParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&{attribute[..split]} {dbref}={attribute[(split + 1)..]}"));
		}

		return dbref;
	}

	private async Task Check(string function, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(function));
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	/// <summary>A drawing compared line by line, without the padding a layout adds to the right.</summary>
	private async Task CheckDrawing(string function, params string[] expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(function));
		var lines = result.ToString().Split('\n').Select(line => line.TrimEnd());
		await Assert.That(string.Join('\n', lines)).IsEqualTo(string.Join('\n', expected));
	}

	[Test]
	public async Task ReachWalksInTheOrderNamed()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL)", "DUAL RIPOSTE SWORDS PARRY");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,bfs starts)", "WHIRL DUAL RIPOSTE SWORDS PARRY");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,dfs starts)", "WHIRL DUAL SWORDS RIPOSTE PARRY");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,post starts)", "SWORDS DUAL PARRY RIPOSTE WHIRL");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,post)", "SWORDS DUAL PARRY RIPOSTE");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,layers starts,,,%b,/)", "SWORDS/DUAL PARRY/RIPOSTE/WHIRL");
	}

	[Test]
	public async Task ReachShapesItsOutput()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`SUBS,CROWN,bfs starts,,,%b,/)", "CROWN/GUARD COURT/NIGHTWATCH");
		await Check($"graph_reach({lab}/FUN`SUBS,CROWN,layers starts,,,%b,/)", "COURT NIGHTWATCH/GUARD/CROWN");
		await Check($"graph_reach({lab}/FUN`SUBS,CROWN,dfs starts,,,|,,{lab}/FUN`INDENT)", "CROWN|  GUARD|    NIGHTWATCH|  COURT");
		// Without a level separator, levels are joined like nodes.
		await Check($"graph_reach({lab}/FUN`SUBS,CROWN,bfs starts,,,|)", "CROWN|GUARD|COURT|NIGHTWATCH");
		// %2 is the node a node was reached from; empty for a start.
		await Check($"graph_reach({lab}/FUN`SUBS,CROWN,bfs starts,,,%b,,#lambda/\\%0<\\%2)", "CROWN< GUARD<CROWN COURT<CROWN NIGHTWATCH<GUARD");
	}

	[Test]
	public async Task ReachDepthCountsTheFewestLinks()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,bfs,1)", "DUAL RIPOSTE");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,dfs,2)", "DUAL SWORDS RIPOSTE PARRY");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,bfs,0)", "");
		// D is two links from A either way; dfs reaches it through B first.
		await Check($"graph_reach({lab}/FUN`DIAMOND,A,dfs starts,1,|)", "A B C");
		await Check($"graph_reach({lab}/FUN`DIAMOND,A,bfs starts,,|,%b,/)", "A/B C/D");
	}

	[Test]
	public async Task AListedStartIsLevelZeroEvenWhenAnotherStartLinksToIt()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`DIAMOND,A|B,dfs starts,,|,|,,{lab}/FUN`VISIT)", "A:0:|B:0:|D:1:B|C:1:A");
		await Check($"graph_reach({lab}/FUN`DIAMOND,A|B,post starts,,|,|,,{lab}/FUN`VISIT)", "D:1:B|B:0:|C:1:A|A:0:");
		await Check($"graph_reach({lab}/FUN`DIAMOND,A|B,layers starts,,|,|,,{lab}/FUN`VISIT)", "D:0:B|B:1:|C:1:A|A:2:");
		// Without starts, B is listed only because A links to it, so it keeps that link.
		await Check($"graph_reach({lab}/FUN`DIAMOND,A|B,dfs,,|,|,,{lab}/FUN`VISIT)", "B:1:A|D:2:B|C:1:A");
	}

	[Test]
	public async Task ReachKeysIgnoreCaseAndKeepTheFirstSpelling()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`NEEDS,whirl,post starts)", "SWORDS DUAL PARRY RIPOSTE whirl");
		await Check($"graph_reach(#lambda/a A b,x)", "a b");
	}

	[Test]
	public async Task LoopsAreNamedOrSkipped()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`QNEEDS,VAULT,post starts)", "FIRSTSTEPS LANTERN KEY VAULT");
		await Check($"[setq(editing,FIRSTSTEPS,proposed,VAULT)][graph_reach({lab}/FUN`QNEEDS,FIRSTSTEPS,post starts)]",
			"#-1 CYCLE FIRSTSTEPS VAULT LANTERN FIRSTSTEPS");
		await Check($"[setq(editing,FIRSTSTEPS,proposed,VAULT)][graph_reach({lab}/FUN`QNEEDS,FIRSTSTEPS,layers)]",
			"#-1 CYCLE FIRSTSTEPS VAULT LANTERN FIRSTSTEPS");
		// bfs skips nodes already seen, and lists a start a loop returns to: a one-call loop test.
		await Check($"member(graph_reach({lab}/FUN`QNEEDS,FIRSTSTEPS),FIRSTSTEPS)", "0");
		await Check($"[setq(editing,FIRSTSTEPS,proposed,VAULT)][member(graph_reach({lab}/FUN`QNEEDS,FIRSTSTEPS),FIRSTSTEPS)]", "4");
		await Check($"graph_reach({lab}/FUN`SELF,A)", "A");
		await Check($"graph_reach({lab}/FUN`SELF,A,post)", "#-1 CYCLE A A");
		await Check($"graph_fold({lab}/FUN`SELF,A,{lab}/FUN`WHERE)", "#-1 CYCLE A A");
	}

	[Test]
	public async Task ReachFromSeveralStarts()
	{
		var lab = await CreateLab();

		// LANTERN is a start, but VAULT's walk reaches it by a link, so it is listed.
		await Check($"graph_reach({lab}/FUN`QNEEDS,VAULT LANTERN,post)", "FIRSTSTEPS LANTERN KEY");
		await Check($"graph_reach({lab}/FUN`QNEEDS,VAULT LANTERN KEY,post starts)", "FIRSTSTEPS LANTERN KEY VAULT");
	}

	[Test]
	public async Task GraphFunctionsRefuseBadArguments()
	{
		var lab = await CreateLab();

		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,sideways)", "#-1 NO SUCH OPTION");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,bfs dfs)", "#-1 CONFLICTING OPTIONS");
		await Check($"graph_reach({lab}/FUN`NEEDS,WHIRL,,-1)", "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER");
		await Check($"graph_reach({lab}/NOSUCH,WHIRL)", "#-1 NO SUCH ATTRIBUTE");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,dfs)", "#-1 NO SUCH OPTION");
		await Check($"graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`WHERE,,sideways)", "#-1 NO SUCH OPTION");
	}

	[Test]
	public async Task ALinkFunctionErrorStopsTheWalk()
	{
		var lab = await CreateLab();

		// Read as keys, this would be three nodes.
		await Check("graph_reach(#lambda/#-1 PERMISSION DENIED,X)", "#-1 PERMISSION DENIED");
		await Check($"graph_path(#lambda/#-1 NOPE,X,Y)", "#-1 NOPE");
		await Check($"graph_fold(#lambda/#-1 NOPE,X,{lab}/FUN`WHERE)", "#-1 NOPE");
	}

	[Test]
	public async Task PathTakesFewestStepsOrLowestCost()
	{
		var lab = await CreateLab();

		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT)", "DOCKS FORT");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,starts)", "HARBOR DOCKS FORT");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,starts,{lab}/FUN`COST)", "HARBOR DOCKS MARKET FORT");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,{lab}/FUN`COST,,|)", "DOCKS|MARKET|FORT");
		// Several targets: the way to the nearest.
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,MARKET FORT)", "MARKET");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,HARBOR,starts)", "HARBOR");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,HARBOR)", "");
	}

	[Test]
	public async Task PathRefusals()
	{
		var lab = await CreateLab();

		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,NOWHERE)", "#-1 NO PATH");
		await Check($"graph_path({lab}/FUN`WAYS,FORT,HARBOR,,{lab}/FUN`COST)", "#-1 NO PATH");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,#lambda/-1)", "#-1 COST MUST NOT BE NEGATIVE");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,#lambda/x)", "#-1 ARGUMENT MUST BE NUMBER");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,#lambda/NaN)", "#-1 ARGUMENT MUST BE NUMBER");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,#lambda/Infinity)", "#-1 ARGUMENT MUST BE NUMBER");
		await Check($"graph_path({lab}/FUN`WAYS,HARBOR,FORT,,#lambda/1e308)", "#-1 ARGUMENT MUST BE NUMBER");
	}

	[Test]
	public async Task FoldCarriesAValueBesideItsResult()
	{
		var lab = await CreateLab();

		await Check($"graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`COUNT,,,,|)", "7");
		await Check($"graph_fold({lab}/FUN`SUBS,GUARD,{lab}/FUN`COUNT,,,,|)", "5");
		await Check($"graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`WRAP,,,,|)", "CROWN<GUARD<NIGHTWATCH<>>|COURT<>>");
		await CheckDrawing($"tree({{{{\"width\":40,\"guide\":\"ascii\"}}}},graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`DRAW,{lab}/FUN`COUNT,,,|))",
			"CROWN (7)", "|- GUARD (5)", "|  `- NIGHTWATCH (3)", "`- COURT (1)");
	}

	[Test]
	public async Task FoldUpVisitsASharedNodeOnce()
	{
		var lab = await CreateLab();

		// D sits under both B and C; it is folded once and its result used twice.
		await Check($"graph_fold({lab}/FUN`DIAMOND,A,{lab}/FUN`WRAP,,,|)", "A<B<D<>> C<D<>>>");
	}

	[Test]
	public async Task FoldDownGivesEachNodeAllItsParents()
	{
		var lab = await CreateLab();

		await Check($"graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`SHOWRANK,{lab}/FUN`RANK,down starts)", "CROWN=1 GUARD=2 COURT=2 NIGHTWATCH=3");
		await Check($"graph_fold({lab}/FUN`SUBS,CROWN,{lab}/FUN`SHOWRANK,{lab}/FUN`RANK,down)", "GUARD=2 COURT=2 NIGHTWATCH=3");
		await Check($"graph_fold({lab}/FUN`DIAMOND,A,{lab}/FUN`WRAP,,down starts,|,;)", "A<>;B<A<>>;C<A<>>;D<B<A<>>;C<A<>>>");
	}

	[Test]
	public async Task JsonWalkVisitsEveryNodeUnderTheNamedKey()
	{
		var lab = await CreateLab();

		await Check($"json_walk({lab}/FUN`LINE,get({lab}/DATA`API),subskills,|)", "Swords|  Parry|    Riposte|  Dual");
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`API),subskills)",
			"0/1/$ 1/1/$.subskills[0] 2/1/$.subskills[0].subskills[0] 1/2/$.subskills[1]");
		await Check($"ladd(json_walk({lab}/FUN`COSTOF,get({lab}/DATA`API),subskills))", "8");
		// The default key is children; a top-level array is a list of roots.
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`ROOTS))", "0/1/$[0] 1/1/$[0].children[0] 0/2/$[1]");
	}

	[Test]
	public async Task JsonWalkChildrenByPathOrObject()
	{
		var lab = await CreateLab();

		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`DEEP),$.links.items)",
			"0/1/$ 1/1/$.links.items[0] 1/2/$.links.items[1] 2/1/$.links.items[1].links.items[0]");
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`OBJ),kids)",
			"0/1/$ 1/1/$.kids['9x'] 1/2/$.kids.ok 2/1/$.kids.ok.kids['it\\'s']");
		// Every path leads back to its node.
		await Check($"json_walk({lab}/FUN`BACK,get({lab}/DATA`OBJ),kids,|,get({lab}/DATA`OBJ))",
			"{\"kids\":{\"9x\":{},\"ok\":{\"kids\":{\"it's\":{}}}}}|{}|{\"kids\":{\"it\\u0027s\":{}}}|{}");
		// A control character in a key is escaped, so the path still leads back.
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`CTRL),kids)", "0/1/$ 1/1/$.kids['a\\nb']");
		await Check($"json_walk({lab}/FUN`LEAF,get({lab}/DATA`CTRL),kids,|,get({lab}/DATA`CTRL))", "1");
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`CTRL),$\\[)", "#-1 PATH NOT FOUND");
	}

	[Test]
	public async Task JsonWalkPassesExtraArgumentsAndLeavesOutEmptyResults()
	{
		var lab = await CreateLab();

		await Check($"json_walk({lab}/FUN`EXTRA,get({lab}/DATA`ROOTS),,|,a,b)", "a-b|a-b|a-b");
		await Check($"json_walk(#lambda/,get({lab}/DATA`API),subskills)", "");
		await Check($"json_walk({lab}/FUN`WHERE,get({lab}/DATA`NOSUCH)x)", "#-1 BAD ARGUMENT FORMAT TO json_walk");
	}

	[Test]
	public async Task TenThousandNodes()
	{
		// One function call per node, so a 10,000-node walk fits the 25,000-call limit.
		await Check(@"words(graph_reach(#lambda/inc\(\%0\),1,,10000))", "10000");
		await Check(@"last(graph_reach(#lambda/inc\(\%0\),1,post starts,9999))", "1");
		await Check(@"words(graph_path(#lambda/inc\(\%0\),1,10000))", "9999");
		await Check(@"words(graph_path(#lambda/inc\(\%0\),1,10000,,#lambda/1))", "9999");
		await Check(@"graph_fold(#lambda/if\(lt\(\%0\,5000\)\,inc\(\%0\)\),1,#lambda/add\(\%0\,0\%1\))", "12502500");
	}
}
