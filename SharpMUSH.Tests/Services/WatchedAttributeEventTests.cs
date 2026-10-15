using MarkupString;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>@attribute/watch</c>: every set, clear or wipe of a watched attribute, on any object and through whichever command
/// or function made it, runs the event handler's attributes the watch's pattern names. A watch is on an attribute
/// name across the whole world, so each test watches a name of its own and answers it with a handler attribute of its
/// own on the shared #9, which logs changes to a log of its own; the tests run alongside each other and alongside
/// other writers of #9.
/// </summary>
public class WatchedAttributeEventTests : ServerTestBase
{
	private const string EventHandler = "#9";
	private static readonly DBRef God = new(1);

	/// <summary>A thing and an attribute name of the test's own, the #9 attribute that logs its changes, and the log.</summary>
	private sealed record Watch(DBRef Thing, string Attribute, string Hearer, string Log);

	/// <summary>
	/// Creates a thing, a table entry for a fresh attribute name, and a handler on #9 that appends
	/// <c>|attribute:old:new:enactor</c> to a log on #9 for each change it runs for; the enactor is logged by number,
	/// and only changes to the thing are logged. The name is not watched yet.
	/// </summary>
	private async Task<Watch> WatchThing(string prefix, string? hearer = null)
	{
		var thing = DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName(prefix)})"));
		var attribute = TestIsolationHelpers.GenerateUniqueName("WT").ToUpperInvariant();
		var log = $"LOG_{attribute}";
		hearer ??= $"WATCH`{attribute}`LOG";
		await Cmd($"@attribute/access {attribute}=none");
		await Cmd($"&{hearer} {EventHandler}=@assert strmatch(num(%0),#{thing.Number}) ; " +
			$"&{log} {EventHandler}=[get({EventHandler}/{log})]|%1:%2:%3:[num(%4)]");
		return new Watch(thing, attribute, hearer, log);
	}

	private async Task Forget(Watch watch)
	{
		await Cmd($"@attribute/delete {watch.Attribute}");
		await Cmd($"&{watch.Hearer} {EventHandler}");
		await Cmd($"&{watch.Log} {EventHandler}");
	}

	/// <summary>The log, once the handlers queued so far have run.</summary>
	private async Task<string> Logged(Watch watch)
	{
		await WebAppFactoryArg.QueueBarrierAsync();
		return await Eval($"get({EventHandler}/{watch.Log})");
	}

	/// <summary>Runs <paramref name="command"/> as God and answers what God was told meanwhile.</summary>
	private async Task<List<string>> Told(string command)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(god).Skip(before)];
	}

	[Test]
	public async Task SettingAWatchedAttributeRunsTheMatchingHandlerWithItsOldAndNewValues()
	{
		var watch = await WatchThing("WatchSet");
		try
		{
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=10");
			await Cmd($"@attribute/watch {watch.Attribute}=WATCH`{watch.Attribute}`LOG");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=15");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}:10:15:#1")
				.Because("the write before the watch is not announced, and the one after it is");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task AWatchCoversTheNameOnEveryObject()
	{
		var watch = await WatchThing("WatchEvery");
		var other = DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName("WatchOther")})"));
		var log = $"LOG2_{watch.Attribute}";
		var hearer = $"WATCH`{watch.Attribute}`OTHER";
		try
		{
			await Cmd($"&{hearer} {EventHandler}=@assert strmatch(num(%0),#{other.Number}) ; &{log} {EventHandler}=%3");
			await Cmd($"@attribute/watch {watch.Attribute}");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=1");
			await Cmd($"&{watch.Attribute} #{other.Number}=2");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}::1:#1");
			await Assert.That(await Eval($"get({EventHandler}/{log})")).IsEqualTo("2")
				.Because("the watch is on the attribute's name, so a copy on any object is watched");
		}
		finally
		{
			await Forget(watch);
			await Cmd($"&{hearer} {EventHandler}");
			await Cmd($"&{log} {EventHandler}");
		}
	}

	[Test]
	public async Task AnUnwatchedAttributeRunsNothing()
	{
		var watched = await WatchThing("WatchOne");
		var unwatched = await WatchThing("WatchNone");
		try
		{
			await Cmd($"@attribute/watch {watched.Attribute}=WATCH`{watched.Attribute}`LOG");
			// The unwatched name's handler is in place, under the very name a watch on it would run; only its watch is missing.
			await Cmd($"&{unwatched.Attribute} #{unwatched.Thing.Number}=5");
			await Cmd($"&{unwatched.Attribute} #{unwatched.Thing.Number}=6");
			await Cmd($"&{unwatched.Attribute} #{unwatched.Thing.Number}");
			await Cmd($"&{watched.Attribute} #{watched.Thing.Number}=2");

			await Assert.That(await Logged(watched)).IsEqualTo($"|{watched.Attribute}::2:#1")
				.Because("the watched write proves the handlers run once the queue has drained");
			await Assert.That(await Logged(unwatched)).IsEmpty();
		}
		finally
		{
			await Forget(watched);
			await Forget(unwatched);
		}
	}

	[Test]
	public async Task ClearingAWatchedAttributeRunsTheHandlerWithAnEmptyNewValue()
	{
		var watch = await WatchThing("WatchClear");
		try
		{
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=7");
			await Cmd($"@attribute/watch {watch.Attribute}");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}:7::#1");
			await Assert.That(await Eval($"hasattr(#{watch.Thing.Number},{watch.Attribute})")).IsEqualTo("0");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task WipingRunsTheHandlerForEachWatchedAttributeItRemoves()
	{
		var root = TestIsolationHelpers.GenerateUniqueName("WR").ToUpperInvariant();
		var watch = await WatchThing("WatchWipe", hearer: $"WATCH`{root}`HP`LOG");
		var thing = $"#{watch.Thing.Number}";
		try
		{
			await Cmd($"@attribute/access {root}`HP=none");
			await Cmd($"@attribute/access {root}`MP=none");
			await Cmd($"&{root} {thing}=root");
			await Cmd($"&{root}`HP {thing}=3");
			await Cmd($"&{root}`MP {thing}=4");
			await Cmd($"@attribute/watch {root}`HP");
			await Cmd($"@wipe {thing}/{root}");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{root}`HP:3::#1")
				.Because($"{root} and {root}`MP are not watched, and a watch covers only the name it is on");
		}
		finally
		{
			await Forget(watch);
			await Cmd($"@attribute/delete {root}`HP");
			await Cmd($"@attribute/delete {root}`MP");
		}
	}

	[Test]
	public async Task TheSetFunctionAndTheSetCommandBothRunIt()
	{
		var watch = await WatchThing("WatchPaths");
		var thing = $"#{watch.Thing.Number}";
		try
		{
			await Cmd($"&{watch.Attribute} {thing}=1");
			await Cmd($"@attribute/watch {watch.Attribute}");
			await Eval($"set({thing},{watch.Attribute}:2)");
			await Cmd($"@set {thing}={watch.Attribute}:3");
			await Cmd($"&SOURCE {thing}=4");
			await Cmd($"@cpattr {thing}/SOURCE={thing}/{watch.Attribute}");

			await Assert.That(await Logged(watch))
				.IsEqualTo($"|{watch.Attribute}:1:2:#1|{watch.Attribute}:2:3:#1|{watch.Attribute}:3:4:#1")
				.Because("set(), @set <object>=<attribute>:<value> and @cpattr each write through the one announcing path");
		}
		finally
		{
			await Forget(watch);
		}
	}

	/// <summary>
	/// <c>@attribute/watch &lt;attribute&gt;</c> with no pattern runs <c>WATCH`&lt;attribute&gt;</c> and every attribute
	/// one level below it, so each package can keep a handler of its own, and gives each handler %0-%4 by name too.
	/// </summary>
	[Test]
	public async Task TheDefaultPatternRunsTheNamedHandlerAndEachOneBelowIt()
	{
		var watch = await WatchThing("WatchDefault");
		var top = $"WATCH`{watch.Attribute}";
		var pkg = $"WATCH`{watch.Attribute}`PKG";
		var deep = $"WATCH`{watch.Attribute}`PKG`DEEP";
		var log = $"LOGD_{watch.Attribute}";
		try
		{
			var set = await Told($"@attribute/watch {watch.Attribute}");
			await Assert.That(set).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandWatchSetFormat,
				watch.Attribute, AttributeWatch.DefaultPattern(watch.Attribute)));

			await Cmd($"&{top} {EventHandler}=&{log} {EventHandler}=[get({EventHandler}/{log})]|top:%<attribute>:%<old>:%<new>");
			await Cmd($"&{pkg} {EventHandler}=&{log} {EventHandler}=[get({EventHandler}/{log})]|pkg:[num(%<objid>)]:[num(%<enactor>)]");
			await Cmd($"&{deep} {EventHandler}=&{log} {EventHandler}=[get({EventHandler}/{log})]|deep");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=9");
			await WebAppFactoryArg.QueueBarrierAsync();

			var logged = (await Eval($"get({EventHandler}/{log})")).Split('|', StringSplitOptions.RemoveEmptyEntries);
			await Assert.That(logged).IsEquivalentTo([$"top:{watch.Attribute}::9", $"pkg:#{watch.Thing.Number}:#1"])
				.Because("one level below WATCH`<attribute> runs, and two levels below does not");
			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}::9:#1");
		}
		finally
		{
			await Forget(watch);
			await Cmd($"@wipe {EventHandler}/{top}");
			await Cmd($"&{log} {EventHandler}");
		}
	}

	[Test]
	public async Task AnEmptyPatternTurnsWatchingOff()
	{
		var watch = await WatchThing("WatchOff");
		try
		{
			await Cmd($"@attribute/watch {watch.Attribute}");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=1");

			var off = await Told($"@attribute/watch {watch.Attribute}=");
			await Assert.That(off).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandWatchRemovedFormat, watch.Attribute));
			await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(watch.Attribute)))?.Watch).IsNull();
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=2");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}::1:#1")
				.Because("the write after the watch was turned off runs nothing");

			var again = await Told($"@attribute/watch {watch.Attribute}=");
			await Assert.That(again).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandWatchNoneFormat, watch.Attribute));
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task TheAttributeMustBeInTheTable()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("WTNONE").ToUpperInvariant();
		var told = await Told($"@attribute/watch {name}");

		await Assert.That(told).Contains(ErrorMessages.Notifications.AttributeCommandNotInTableUseAccess);
		await Assert.That(await Mediator.Send(new GetAttributeEntryQuery(name))).IsNull()
			.Because("as /limit and /enum, /watch does not create the entry");
	}

	[Test]
	public async Task TheEntryShowsAndDecompilesItsWatch()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("WTSHOW").ToUpperInvariant();
		try
		{
			await Cmd($"@attribute/access {name}=no_command");
			await Cmd($"@attribute/watch {name}=watch`{name}`a  WATCH`{name}`B*");

			var shown = await Told($"@attribute {name}");
			await Assert.That(shown).Contains($"  Watch: WATCH`{name}`A WATCH`{name}`B*");

			var decompiled = await Told($"@attribute/decompile {name}");
			await Assert.That(decompiled).Contains($"@attribute/watch {name}=WATCH`{name}`A WATCH`{name}`B*");

			// Setting the entry's flags again leaves its watch as it was.
			await Cmd($"@attribute/access {name}=wizard");
			await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))?.Watch)
				.IsEqualTo($"WATCH`{name}`A WATCH`{name}`B*");
		}
		finally
		{
			await Cmd($"@attribute/delete {name}");
		}
	}

	[Test]
	public async Task AttrwatchAnswersWhetherTheAttributeIsInTheTableAndHowItIsWatched()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("WTREAD").ToUpperInvariant();
		try
		{
			await Assert.That(await Eval($"attrwatch({name})")).IsEqualTo(ErrorMessages.Returns.NotStandardAttribute)
				.Because("the name is not in the table yet");

			await Cmd($"@attribute/access {name}=none");
			await Assert.That(await Eval($"attrwatch({name})")).IsEqualTo(string.Empty)
				.Because("in the table and not watched");

			await Cmd($"@attribute/watch {name}");
			await Assert.That(await Eval($"attrwatch({name.ToLowerInvariant()})")).IsEqualTo($"WATCH`{name} WATCH`{name}`*")
				.Because("the default pattern, whatever case the name is asked in");

			await Cmd($"@attribute/watch {name}=");
			await Assert.That(await Eval($"attrwatch({name})")).IsEqualTo(string.Empty)
				.Because("an empty pattern turned watching off");
		}
		finally
		{
			await Cmd($"@attribute/delete {name}");
		}
	}

	[Test]
	public async Task OnlyConfigAdminsMayWatch()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(God)))
			.Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("WatchMortalRoom"), god));
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WatchMortal", room);
		var name = TestIsolationHelpers.GenerateUniqueName("WTDENY").ToUpperInvariant();
		try
		{
			await Cmd($"@attribute/access {name}=none");
			await CmdAs(mortal.DbRef, mortal.Handle, $"@attribute/watch {name}");

			await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))?.Watch).IsNull();
		}
		finally
		{
			await Cmd($"@attribute/delete {name}");
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task TheEnactorIsWhoeverWroteIt()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(God)))
			.Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("WatchRoom"), god));
		var writer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WatchWriter", room);
		var watch = await WatchThing("WatchEnactor");
		try
		{
			await Cmd($"@chown #{watch.Thing.Number}=#{writer.DbRef.Number}");
			await Cmd($"&{watch.Attribute} #{watch.Thing.Number}=5");
			await Cmd($"@attribute/watch {watch.Attribute}");
			await CmdAs(writer.DbRef, writer.Handle, $"&{watch.Attribute} #{watch.Thing.Number}=4");

			await Assert.That(await Logged(watch)).IsEqualTo($"|{watch.Attribute}:5:4:#{writer.DbRef.Number}");
		}
		finally
		{
			await Forget(watch);
			await ConnectionService.Disconnect(writer.Handle);
		}
	}

	/// <summary>
	/// <c>USE</c> spends a charge by writing <c>CHARGES</c> under the attribute's owner; the player who used the
	/// object is still the change's enactor. CHARGES is a standard attribute, so the watch is taken off again after.
	/// </summary>
	[Test]
	public async Task UsingAnObjectNamesItsUserAsTheChargesEnactor()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(God)))
			.Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("WatchUseRoom"), god));
		var user = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WatchUser", room);
		var suffix = TestIsolationHelpers.GenerateUniqueName("USE").ToUpperInvariant();
		var watch = await WatchThing("WatchUse", hearer: $"WATCH`CHARGES`{suffix}");
		var had = (await Mediator.Send(new GetAttributeEntryQuery("CHARGES")))?.Watch;
		try
		{
			await Cmd($"@tel #{watch.Thing.Number}=#{room.Number}");
			await Cmd($"&CHARGES #{watch.Thing.Number}=2");
			await Cmd($"@attribute/watch CHARGES=WATCH`CHARGES`{suffix}");
			await CmdAs(user.DbRef, user.Handle, $"use #{watch.Thing.Number}");

			await Assert.That(await Logged(watch)).IsEqualTo($"|CHARGES:2:1:#{user.DbRef.Number}")
				.Because("the charge is spent by the player's USE, not by God, whose name the write is made under");
		}
		finally
		{
			await Cmd($"@attribute/watch CHARGES={had}");
			await Cmd($"&{watch.Hearer} {EventHandler}");
			await Cmd($"&{watch.Log} {EventHandler}");
			await Cmd($"@attribute/delete {watch.Attribute}");
			await ConnectionService.Disconnect(user.Handle);
		}
	}

	/// <summary>
	/// A handler that changes the attribute it heard about runs again for its own change, until the chain is
	/// <see cref="AttributeChangeChain.MaxDepth"/> handlers long: the change the last one makes is written and
	/// runs nobody.
	/// </summary>
	[Test]
	public async Task AHandlerThatChangesWhatItHeardStopsAtTheChainLimit()
	{
		var thing = DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName("WatchLoop")})"));
		var attribute = TestIsolationHelpers.GenerateUniqueName("WTLOOP").ToUpperInvariant();
		var hearer = $"WATCH`{attribute}";
		var count = $"COUNT_{attribute}";
		try
		{
			await Cmd($"@attribute/access {attribute}=none");
			await Cmd($"&{attribute} #{thing.Number}=start");
			await Cmd($"@attribute/watch {attribute}");
			await Cmd($"&{hearer} {EventHandler}=@assert strmatch(num(%0),#{thing.Number}) ; " +
				$"&{count} {EventHandler}=[add(default({EventHandler}/{count},0),1)] ; &{attribute} %0=[add(%3,1)]");
			await Cmd($"&{attribute} #{thing.Number}=0");

			// Each handler is queued by the one before it, so wait out the chain one barrier at a time.
			for (var round = 0; round < AttributeChangeChain.MaxDepth * 3; round++)
			{
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(await Eval($"get({EventHandler}/{count})"))
				.IsEqualTo(AttributeChangeChain.MaxDepth.ToString())
				.Because("the typed change and each handler's change up to the limit run the handler once apiece");
			await Assert.That(await Eval($"get(#{thing.Number}/{attribute})"))
				.IsEqualTo(AttributeChangeChain.MaxDepth.ToString())
				.Because("the last handler's change is still written, only not announced");
		}
		finally
		{
			await Cmd($"@attribute/delete {attribute}");
			await Cmd($"&{hearer} {EventHandler}");
			await Cmd($"&{count} {EventHandler}");
		}
	}
}
