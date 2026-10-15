using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>ATTRIBUTE`CHANGE</c>: raised for every write to an attribute flagged <c>watched</c>, through whichever command
/// or function made it, and for no other attribute. Each test watches a thing of its own and answers it with an
/// event handler attribute of its own on the shared #9 (<c>ATTRIBUTE`CHANGE`&lt;unique&gt;</c>), which only logs
/// changes to that thing, so the tests run alongside each other and alongside other writers of #9.
/// </summary>
public class WatchedAttributeEventTests : ServerTestBase
{
	private const string EventHandler = "#9";

	/// <summary>A thing of the test's own, and the #9 attributes that hear and log its changes.</summary>
	private sealed record Watch(DBRef Thing, string Hearer, string Log);

	/// <summary>
	/// Creates a thing and a hearer that appends <c>|attribute:old:new:enactor</c> to a log on #9 for each change to
	/// one of the thing's watched attributes. The enactor is logged by number.
	/// </summary>
	private async Task<Watch> WatchThing(string prefix)
	{
		var thing = DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName(prefix)})"));
		var suffix = TestIsolationHelpers.GenerateUniqueName("WATCH").ToUpperInvariant();
		var hearer = $"ATTRIBUTE`CHANGE`{suffix}";
		var log = $"LOG_{suffix}";
		await Cmd($"&{hearer} {EventHandler}=@assert strmatch(num(%0),#{thing.Number}) ; " +
			$"&{log} {EventHandler}=[get({EventHandler}/{log})]|%1:%2:%3:[num(%4)]");
		return new Watch(thing, hearer, log);
	}

	private async Task Forget(Watch watch)
	{
		await Cmd($"&{watch.Hearer} {EventHandler}");
		await Cmd($"&{watch.Log} {EventHandler}");
	}

	/// <summary>The log, once the events queued so far have run.</summary>
	private async Task<string> Logged(Watch watch)
	{
		await WebAppFactoryArg.QueueBarrierAsync();
		return await Eval($"get({EventHandler}/{watch.Log})");
	}

	[Test]
	public async Task AWatchedAttributesChangeIsRaisedWithItsOldAndNewValues()
	{
		var watch = await WatchThing("WatchSet");
		try
		{
			await Cmd($"&HP #{watch.Thing.Number}=10");
			await Cmd($"@set #{watch.Thing.Number}/HP=watched");
			await Cmd($"&HP #{watch.Thing.Number}=15");

			await Assert.That(await Logged(watch)).IsEqualTo("|HP:10:15:#1")
				.Because("the write before the flag was set is not watched, and the one after it is");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task AnUnwatchedAttributeRaisesNothing()
	{
		var watch = await WatchThing("WatchNone");
		try
		{
			await Cmd($"&MP #{watch.Thing.Number}=5");
			await Cmd($"&MP #{watch.Thing.Number}=6");
			await Cmd($"&MP #{watch.Thing.Number}");
			await Cmd($"&HP #{watch.Thing.Number}=1");
			await Cmd($"@set #{watch.Thing.Number}/HP=watched");
			await Cmd($"&HP #{watch.Thing.Number}=2");

			await Assert.That(await Logged(watch)).IsEqualTo("|HP:1:2:#1")
				.Because("only the write to the watched HP is announced; the hearer itself is proven by it");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task ClearingAWatchedAttributeRaisesWithAnEmptyNewValue()
	{
		var watch = await WatchThing("WatchClear");
		try
		{
			await Cmd($"&HP #{watch.Thing.Number}=7");
			await Cmd($"@set #{watch.Thing.Number}/HP=watched");
			await Cmd($"&HP #{watch.Thing.Number}");

			await Assert.That(await Logged(watch)).IsEqualTo("|HP:7::#1");
			await Assert.That(await Eval($"hasattr(#{watch.Thing.Number},HP)")).IsEqualTo("0");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task WipingRaisesForEachWatchedAttributeItRemoves()
	{
		var watch = await WatchThing("WatchWipe");
		try
		{
			await Cmd($"&STATS #{watch.Thing.Number}=root");
			await Cmd($"&STATS`HP #{watch.Thing.Number}=3");
			await Cmd($"&STATS`MP #{watch.Thing.Number}=4");
			await Cmd($"@set #{watch.Thing.Number}/STATS`HP=watched");
			await Cmd($"@wipe #{watch.Thing.Number}/STATS");

			await Assert.That(await Logged(watch)).IsEqualTo("|STATS`HP:3::#1")
				.Because("STATS and STATS`MP are not watched, and the flag does not reach below the attribute it is on");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task TheSetFunctionAndTheSetCommandBothRaise()
	{
		var watch = await WatchThing("WatchPaths");
		try
		{
			await Cmd($"&HP #{watch.Thing.Number}=1");
			await Cmd($"@set #{watch.Thing.Number}/HP=watched");
			await Eval($"set(#{watch.Thing.Number},HP:2)");
			await Cmd($"@set #{watch.Thing.Number}=HP:3");
			await Cmd($"&SOURCE #{watch.Thing.Number}=4");
			await Cmd($"@cpattr #{watch.Thing.Number}/SOURCE=#{watch.Thing.Number}/HP");

			await Assert.That(await Logged(watch)).IsEqualTo("|HP:1:2:#1|HP:2:3:#1|HP:3:4:#1")
				.Because("set(), @set <object>=<attribute>:<value> and @cpattr each write through the one announcing path");
		}
		finally
		{
			await Forget(watch);
		}
	}

	[Test]
	public async Task TheEnactorIsWhoeverWroteIt()
	{
		var god = (await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(new DBRef(1))))
			.Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("WatchRoom"), god));
		var writer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WatchWriter", room);
		var watch = await WatchThing("WatchEnactor");
		try
		{
			await Cmd($"@chown #{watch.Thing.Number}=#{writer.DbRef.Number}");
			await Cmd($"&HP #{watch.Thing.Number}=5");
			await Cmd($"@set #{watch.Thing.Number}/HP=watched");
			await CmdAs(writer.DbRef, writer.Handle, $"&HP #{watch.Thing.Number}=4");

			await Assert.That(await Logged(watch)).IsEqualTo($"|HP:5:4:#{writer.DbRef.Number}");
		}
		finally
		{
			await Forget(watch);
			await ConnectionService.Disconnect(writer.Handle);
		}
	}

	/// <summary>
	/// <c>USE</c> spends a charge by writing <c>CHARGES</c> under the attribute's owner; the player who used the
	/// object is still the change's enactor.
	/// </summary>
	[Test]
	public async Task UsingAnObjectNamesItsUserAsTheChargesEnactor()
	{
		var god = (await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(new DBRef(1))))
			.Expect<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("WatchUseRoom"), god));
		var user = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WatchUser", room);
		var watch = await WatchThing("WatchUse");
		try
		{
			await Cmd($"@tel #{watch.Thing.Number}=#{room.Number}");
			await Cmd($"&CHARGES #{watch.Thing.Number}=2");
			await Cmd($"@set #{watch.Thing.Number}/CHARGES=watched");
			await CmdAs(user.DbRef, user.Handle, $"use #{watch.Thing.Number}");

			await Assert.That(await Logged(watch)).IsEqualTo($"|CHARGES:2:1:#{user.DbRef.Number}")
				.Because("the charge is spent by the player's USE, not by God, whose name the write is made under");
		}
		finally
		{
			await Forget(watch);
			await ConnectionService.Disconnect(user.Handle);
		}
	}

	/// <summary>
	/// A hearer that raises the attribute it heard about runs again for its own change, until the chain is
	/// <see cref="AttributeChangeChain.MaxDepth"/> events long: the change the last one makes is written and
	/// announced to nobody.
	/// </summary>
	[Test]
	public async Task AHandlerThatChangesWhatItHeardStopsAtTheChainLimit()
	{
		var thing = DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName("WatchLoop")})"));
		var suffix = TestIsolationHelpers.GenerateUniqueName("WATCHLOOP").ToUpperInvariant();
		var hearer = $"ATTRIBUTE`CHANGE`{suffix}";
		var count = $"COUNT_{suffix}";
		try
		{
			await Cmd($"&HP #{thing.Number}=start");
			await Cmd($"@set #{thing.Number}/HP=watched");
			await Cmd($"&{hearer} {EventHandler}=@assert strmatch(num(%0),#{thing.Number}) ; " +
				$"&{count} {EventHandler}=[add(default({EventHandler}/{count},0),1)] ; &HP %0=[add(%3,1)]");
			await Cmd($"&HP #{thing.Number}=0");

			// Each event is queued by the one before it, so wait out the chain one barrier at a time.
			for (var round = 0; round < AttributeChangeChain.MaxDepth * 3; round++)
			{
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(await Eval($"get({EventHandler}/{count})"))
				.IsEqualTo(AttributeChangeChain.MaxDepth.ToString())
				.Because("the typed change and each handler's change up to the limit raise one event apiece");
			await Assert.That(await Eval($"get(#{thing.Number}/HP)"))
				.IsEqualTo(AttributeChangeChain.MaxDepth.ToString())
				.Because("the last handler's change is still written, only not announced");
		}
		finally
		{
			await Cmd($"&{hearer} {EventHandler}");
			await Cmd($"&{count} {EventHandler}");
		}
	}
}
