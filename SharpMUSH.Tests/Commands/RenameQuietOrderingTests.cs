using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>do_name</c> ends with <c>queue_event(...OBJECT`RENAME...)</c> and then
/// <c>if (!AreQuiet(player, thing)) notify(player, T("Name set."))</c> (<c>src/set.c:151-154</c>).
/// <c>queue_event</c> only enqueues, so <c>AreQuiet</c> is answered against the state the rename
/// found, even when the handler goes on to flag the thing <c>QUIET</c>.
/// </summary>
/// <remarks>
/// <c>event_handler = 9</c> (the seeded Event Handler, a WIZARD) in the test config. The renamer is a
/// fresh player in a room of its own, renaming a thing it created: <c>AreQuiet</c>'s second half is
/// <c>Quiet(thing) &amp;&amp; Owner(thing) == player</c>. The handler flags only that thing, so another
/// test's rename that reaches it changes nothing. Writing the handler's attribute is still a global
/// write, so the class does not run alongside the other tests that write #9's event attributes.
/// </remarks>
[NotInParallel]
public class RenameQuietOrderingTests : ServerTestBase
{
	private const int EventHandlerDbRefNumber = 9;
	private static readonly DBRef God = new(1);

	/// <summary>A connected player standing in a room of its own, holding a thing it created.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Renamer, DBRef Thing, string Name)> RenamerWithThing()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(God))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("RqoRoom"), god));
		var renamer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RqoRenamer", room);

		var name = TestIsolationHelpers.GenerateUniqueName("RqoThing");
		await CmdAs(renamer.DbRef, renamer.Handle, $"@create {name}");
		var thing = DBRef.Parse(await EvalAs(renamer.DbRef, $"locate(me,{name},i)"));
		await Assert.That(thing.Number).IsGreaterThan(0).Because("the renamer must hold the thing it created");
		return (renamer, thing, name);
	}

	/// <summary>What <paramref name="renamer"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer renamer, string command)
	{
		var before = Notifications.CountFor(renamer.DbRef);
		await CmdAs(renamer.DbRef, renamer.Handle, command);
		return [.. Notifications.For(renamer.DbRef).Skip(before)];
	}

	private async Task<bool> IsQuiet(DBRef thing) => await Eval($"hasflag(#{thing.Number},QUIET)") == "1";

	/// <summary>The handler's entry runs from the queue, after the rename has answered.</summary>
	private async Task<bool> BecomesQuiet(DBRef thing)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (await IsQuiet(thing)) return true;
			await Task.Delay(50);
		}

		return false;
	}

	[Test]
	public async ValueTask ARenameHandlerThatSetsQuietDoesNotSwallowItsOwnNameSet()
	{
		var (renamer, thing, name) = await RenamerWithThing();
		var renamed = TestIsolationHelpers.GenerateUniqueName("RqoRenamed");

		try
		{
			await Cmd($"&OBJECT`RENAME #{EventHandlerDbRefNumber}=@switch num(%0)=#{thing.Number},@set %0=QUIET");

			var heard = await Heard(renamer, $"@name #{thing.Number}={renamed}");

			await Assert.That(await BecomesQuiet(thing)).IsTrue()
				.Because("the handler has to have run for the ordering to be under test at all");
			await Assert.That(heard).Contains("Name set.")
				.Because("the handler's QUIET arrives after queue_event, so it cannot answer this rename's AreQuiet");

			// And the flag it set does hold for the next rename, which is AreQuiet's other half:
			// Quiet(thing) && Owner(thing) == player.
			await Assert.That(await Heard(renamer, $"@name #{thing.Number}={name}")).DoesNotContain("Name set.");
		}
		finally
		{
			await Cmd($"@wipe #{EventHandlerDbRefNumber}/OBJECT`RENAME");
			await ConnectionService.Disconnect(renamer.Handle);
		}
	}
}
