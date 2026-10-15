using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>indexed()</c>, <c>lsearch()</c>'s <c>INDEX</c> class and <c>@attribute/index</c>, end to end. Each test indexes an
/// attribute of its own, so the shared world's other objects never match.
/// </summary>
public class IndexedFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private static string AttributeName(string prefix)
		=> (prefix + "_" + Guid.NewGuid().ToString("N"))[..24].ToUpperInvariant();

	private async Task<List<string>> AsGod(string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(God);
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(God).Skip(before)];
	}

	private async Task<List<string>> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before)];
	}

	private async Task<string> Eval(string expression, DBRef? executor = null)
		=> (await (executor is { } who ? WebAppFactoryArg.FunctionParserFor(who) : WebAppFactoryArg.FunctionParser)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task<DBRef> ThingAsync(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, prefix);

	/// <summary>An attribute in the table with an index built over it, and a thing per value holding it.</summary>
	private async Task<(string Name, List<DBRef> Things)> IndexedAsync(string prefix, string options, params string[] values)
	{
		var name = AttributeName(prefix);
		await AsGod($"@attribute/access {name}=none");
		await AsGod(options.Length == 0 ? $"@attribute/index {name}" : $"@attribute/index {name}={options}");
		var things = new List<DBRef>();
		foreach (var value in values)
		{
			var thing = await ThingAsync(prefix);
			await AsGod($"&{name} {thing}={value}");
			things.Add(thing);
		}

		return (name, things);
	}

	private static string Refs(IEnumerable<DBRef> things) => string.Join(' ', things.Select(thing => $"#{thing.Number}"));

	[Test]
	public async Task TheOperationsAnswerInIndexOrder()
	{
		var (name, t) = await IndexedAsync("IdxOps", "numeric", "10", "9", "05", "abc", "5");

		await Assert.That(await Eval($"indexed({name},first)")).IsEqualTo(Refs([t[2], t[4], t[1], t[0], t[3]]));
		await Assert.That(await Eval($"indexed({name},last,,1,2)")).IsEqualTo(Refs([t[3], t[0]]));
		await Assert.That(await Eval($"indexed({name},=,5)")).IsEqualTo(Refs([t[2], t[4]]));
		await Assert.That(await Eval($"indexed({name},between,6 10)")).IsEqualTo(Refs([t[1], t[0]]));
		await Assert.That(await Eval($"indexed({name},>=,9)")).IsEqualTo(Refs([t[1], t[0], t[3]]));
		await Assert.That(await Eval($"indexed({name},<,9)")).IsEqualTo(Refs([t[2], t[4]]));
		await Assert.That(await Eval($"indexed({name},prefix,AB)")).IsEqualTo(Refs([t[3]]));
		await Assert.That(await Eval($"indexed({name},count,5)")).IsEqualTo("2");
		// START counts from 1 and COUNT takes that many.
		await Assert.That(await Eval($"indexed({name},first,,2,2)")).IsEqualTo(Refs([t[4], t[1]]));
		await Assert.That(await Eval($"indexed({name},first,,6,2)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task BadCallsSayWhy()
	{
		var (name, _) = await IndexedAsync("IdxBad", "", "x");

		await Assert.That(await Eval($"indexed(NOT_{name},=,x)")).IsEqualTo(ErrorMessages.Returns.NotIndexed);
		await Assert.That(await Eval($"indexed({name},near,x)")).IsEqualTo(ErrorMessages.Returns.InvalidIndexOperation);
		await Assert.That(await Eval($"indexed({name},between,x)")).IsEqualTo(ErrorMessages.Returns.BetweenTakesTwoValues);
		await Assert.That(await Eval($"indexed({name},=,x,0)")).IsEqualTo(ErrorMessages.Returns.PositiveInteger);
	}

	[Test]
	public async Task ABuildingIndexAnswersIndexBuilding()
	{
		var name = AttributeName("IdxBuilding");
		await AsGod($"@attribute/access {name}=none");
		// Put the index on without starting its build.
		await Mediator.Send(new SetAttributeIndexCommand(name, AttributeIndexOptions.Default));

		await Assert.That(await Eval($"indexed({name},=,x)")).IsEqualTo(ErrorMessages.Returns.IndexBuilding);
		await Assert.That(await Eval($"indexed({name},count,x)")).IsEqualTo(ErrorMessages.Returns.IndexBuilding);
		await Assert.That(await Eval($"lsearch(all,index,{name}:x)")).IsEqualTo(ErrorMessages.Returns.IndexBuilding);

		await Mediator.Send(new DropAttributeIndexCommand(name));
	}

	[Test]
	public async Task AMortalSeesOnlyCopiesItCouldRead()
	{
		var (name, t) = await IndexedAsync("IdxVis", "", "gold", "gold", "gold", "gold");
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdxVisMortal");
		await AsGod($"@set {t[1]}/{name}=visual");
		await AsGod($"@set {t[3]}/{name}=visual");
		await AsGod($"@chown {t[2]}={mortal}");

		// God's things are not the mortal's to examine: only the visual copies and its own object are listed.
		await Assert.That(await Eval($"indexed({name},=,gold)", mortal)).IsEqualTo(Refs([t[1], t[2], t[3]]));
		await Assert.That(await Eval($"indexed({name},=,gold,2,1)", mortal)).IsEqualTo(Refs([t[2]]));
		await Assert.That(await Eval($"indexed({name},count,gold)", mortal)).IsEqualTo("3");
		await Assert.That(await Eval($"indexed({name},count,gold)")).IsEqualTo("4");

		// mortal_dark hides a copy even from its owner.
		await AsGod($"@set {t[2]}/{name}=mortal_dark");
		await Assert.That(await Eval($"indexed({name},=,gold)", mortal)).IsEqualTo(Refs([t[1], t[3]]));
		await Assert.That(await Eval($"indexed({name},=,gold)")).IsEqualTo(Refs(t));
	}

	[Test]
	public async Task HiddenHitsAreSkippedAcrossPages()
	{
		var values = Enumerable.Repeat("silver", 300).ToArray();
		var (name, t) = await IndexedAsync("IdxPages", "", values);
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdxPagesMortal");
		await AsGod($"@set {t[^1]}/{name}=visual");

		await Assert.That(await Eval($"indexed({name},=,silver)", mortal)).IsEqualTo(Refs([t[^1]]));
		await Assert.That(await Eval($"words(indexed({name},=,silver))")).IsEqualTo("300");
	}

	[Test]
	public async Task LsearchSeedsFromTheIndex()
	{
		var (name, t) = await IndexedAsync("IdxLsearch", "", "Red", "blue", "red");

		await Assert.That(await Eval($"lsearch(all,index,{name}:red)")).IsEqualTo(Refs([t[0], t[2]]));
		await Assert.That(await Eval($"lsearch(all,type,thing,index,{name}:BLUE)")).IsEqualTo(Refs([t[1]]));
		await Assert.That(await Eval($"lsearch(all,type,player,index,{name}:red)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lsearch(all,index,NOT_{name}:red)")).IsEqualTo(ErrorMessages.Returns.NotIndexed);

		// A mortal finds only objects it may examine whose copy it could read.
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "IdxLsearchMortal");
		await AsGod($"@chown {t[2]}={mortal}");
		await Assert.That(await Eval($"lsearch(all,index,{name}:red)", mortal)).IsEqualTo(Refs([t[2]]));
	}

	[Test]
	public async Task TheCommandShowsDecompilesAndTakesTheIndexOff()
	{
		var name = AttributeName("IdxCmd");
		await AsGod($"@attribute/access {name}=none");
		var set = await AsGod($"@attribute/index {name}=numeric split:| type:thing");
		await Assert.That(set).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandIndexSettingFormat, name,
			"numbers, then text without case, each element of a list split on |, things only"));
		await Assert.That(set.Any(line => line.StartsWith($"Index on {name} ready:"))).IsTrue();

		var entry = await Mediator.Send(new GetAttributeEntryQuery(name));
		await Assert.That(entry?.Index?.Options).IsEqualTo(new AttributeIndexOptions(Numeric: true, Split: "|", Type: "THING"));
		await Assert.That(entry?.Index?.Ready).IsTrue();

		var shown = await AsGod($"@attribute {name}");
		await Assert.That(shown.Any(line => line.Contains("Index: numbers") && line.Contains("(ready)"))).IsTrue();
		var decompiled = await AsGod($"@attribute/decompile {name}");
		await Assert.That(decompiled).Contains($"@attribute/index {name}=numeric split:| type:thing");

		var again = await AsGod($"@attribute/index {name}=numeric split:| type:thing");
		await Assert.That(again.Any(line => line.Contains("already indexed"))).IsTrue();

		var internalRefused = await AsGod($"@attribute/access {name}=internal");
		await Assert.That(internalRefused).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandIndexedNotInternalFormat, name));

		var off = await AsGod($"@attribute/index {name}=off");
		await Assert.That(off).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandIndexRemovedFormat, name));
		await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))?.Index).IsNull();
		await Assert.That(await Eval($"indexed({name},first)")).IsEqualTo(ErrorMessages.Returns.NotIndexed);
	}

	[Test]
	public async Task TheCommandRefusesWhatItCannotIndex()
	{
		var name = AttributeName("IdxRefuse");
		var unknown = await AsGod($"@attribute/index {name}");
		await Assert.That(unknown).Contains(ErrorMessages.Notifications.AttributeCommandNotInTableUseAccess);

		await AsGod($"@attribute/access {name}=internal");
		var internalEntry = await AsGod($"@attribute/index {name}");
		await Assert.That(internalEntry).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandIndexInternalFormat, name));

		await AsGod($"@attribute/access {name}=none");
		var badOptions = await AsGod($"@attribute/index {name}=sorted");
		await Assert.That(badOptions.Any(line => line.StartsWith("Index options:"))).IsTrue();

		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService,
			"IdxRefuseMortal");
		var denied = await As(mortal, $"@attribute/index {name}");
		await Assert.That(denied).Contains("Permission denied.");
		await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))?.Index).IsNull();
	}

	[Test]
	public async Task AnIndexMovesWithARename()
	{
		var (name, t) = await IndexedAsync("IdxRename", "exact", "Moved");
		var renamed = AttributeName("IdxRenamed");
		await AsGod($"@attribute/rename {name}={renamed}");
		await AsGod($"&{renamed} {t[0]}=Moved");

		// The build the rename started runs in the background.
		var deadline = DateTime.UtcNow.AddSeconds(30);
		while ((await Mediator.Send(new GetAttributeEntryQuery(renamed)))?.Index is not { Ready: true } && DateTime.UtcNow < deadline)
		{
			await Task.Delay(50);
		}

		await Assert.That(await Eval($"indexed({renamed},=,Moved)")).IsEqualTo(Refs([t[0]]));
		await Assert.That(await Eval($"indexed({renamed},=,moved)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"indexed({name},=,Moved)")).IsEqualTo(ErrorMessages.Returns.NotIndexed);
	}

	/// <summary>What the host does at startup for an index a restart left building.</summary>
	[Test]
	public async Task ResumingFinishesABuildingIndex()
	{
		var name = AttributeName("IdxResume");
		await AsGod($"@attribute/access {name}=none");
		var thing = await ThingAsync("IdxResume");
		await AsGod($"&{name} {thing}=later");
		await Mediator.Send(new SetAttributeIndexCommand(name, AttributeIndexOptions.Default));

		var builder = WebAppFactoryArg.Services.GetRequiredService<IAttributeIndexBuilder>();
		await builder.ResumeAllAsync();
		await builder.Build(name).Completion;

		await Assert.That(await Eval($"indexed({name},=,later)")).IsEqualTo(Refs([thing]));
	}
}
