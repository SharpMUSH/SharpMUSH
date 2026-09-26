using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Where a matched <c>$</c>-command runs (#1132). PennMUSH's <c>atr_comm_match</c> runs the body in place
/// only for a command typed at a connection (<c>QUEUE_INPLACE</c>, <c>src/game.c:1224-1225</c>); a match
/// reached from softcode is queued by <c>parse_que_attr</c> as a new entry (<c>src/attrib.c:2056-2093</c>).
/// The expected output is what PennMUSH 1.8.8 (<c>95ad3511d</c>) printed for the same softcode.
/// </summary>
[NotInParallel]
public class QueuedUserCommandTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _actor = null!;
	private DBRef _commands;
	private string _token = "";
	private string _room = "";

	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	[Before(Test)]
	public async Task CreateActor()
	{
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "QueuedCmd");
		_room = await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("QueuedCmdRoom")}");
		await Cmd($"@tel {_actor.DbRef}={_room}");
		_commands = await CreateThing("QueuedCmdObj");
		_token = TestIsolationHelpers.GenerateUniqueName("qc").ToLowerInvariant();
	}

	[After(Test)]
	public async Task DisconnectActor()
	{
		// A loop that outlived a failed assertion must not keep running in the shared server.
		await Cmd($"@set {_commands}=halt");
		await Cmd($"@halt {_commands}");
		await ConnectionService.Disconnect(_actor.Handle);
	}

	private Task<string> Run(string command) => CmdAs(_actor.DbRef, _actor.Handle, command);

	private async Task<DBRef> CreateThing(string prefix)
	{
		var thing = DBRef.Parse(await Run($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		await Run($"@set {thing}=!no_command");
		await Run($"drop {thing}");
		return thing;
	}

	/// <summary>Runs <paramref name="list"/> as the actor from the queue, as @trigger would, and waits for it and everything it queued.</summary>
	private async Task RunQueued(string list)
	{
		await Mediator.Send(new AdmitCommandListRequest(
			MString.Plain(list),
			WebAppFactoryArg.CommandParserFor(_actor.DbRef, _actor.Handle).CurrentState,
			new DbRefAttribute(_actor.DbRef, ["QUEUED_TEST"]),
			-1));
		await Scheduler.DrainImmediateQueueForTests();
	}

	private List<string> Heard() => Notifications.For(_actor.DbRef)
		.Where(message => message.Contains(_token, StringComparison.Ordinal))
		.ToList();

	[Test]
	public async Task SoftcodeMatchRunsAfterTheListThatMatchedIt()
	{
		// PennMUSH: `&T me=+hi;@pemit me=after` then `@trigger me/T` prints `after`, then the body.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body");

		await RunQueued($"{_token};@pemit me={_token} after");

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} after", $"{_token} body"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task SoftcodeMatchRedispatchedByAPrefixStillQueues()
	{
		// A prefix such as `~` redispatches its command in the same queue entry, so a list from the queue keeps
		// its origin: the match is not QUEUE_INPLACE even though the entry still knows the connection.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body");

		await RunQueued($"~{_token};@pemit me={_token} after");

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} after", $"{_token} body"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task SoftcodeMatchDoesNotRunWhenTheObjectIsHaltedBeforeItStarts()
	{
		// The rest of the list halts the command object before the queued body gets its turn; the queue
		// re-checks HALT when an entry starts (src/cque.c:1136), so the body never runs.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body");

		await RunQueued($"{_token};@set {_commands}=halt;@pemit me={_token} after");

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} after"]);
	}

	[Test]
	public async Task SoftcodeMatchStartsWithFreshRegistersAndKeepsItsOwn()
	{
		// PennMUSH: `think setq(0,before);+setq;think q0-after=[r(0)];+hi` prints `q0-after=before`, then
		// the body with an empty q0: the queued entry neither sees nor writes the list's registers.
		await Run($"&SET {_commands}=${_token}set:think setq(0,inner)");
		await Run($"&SHOW {_commands}=${_token}show:@pemit %#={_token} q0=[r(0)]");

		await RunQueued($"think setq(0,before);{_token}set;@pemit me={_token} q0-after=[r(0)];{_token}show");

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} q0-after=before", $"{_token} q0="], CollectionOrdering.Matching);
	}

	[Test]
	public async Task SoftcodeMatchKeepsExecutorEnactorCallerAndCaptures()
	{
		// parse_que_attr(thing, player, ...) queues with the command's executor as enactor and caller.
		await Run($"&CMD {_commands}=${_token} *:@pemit %#={_token} me=%! enactor=%# caller=%@ arg=%0");

		await RunQueued($"{_token} word");

		await Assert.That(Heard()).IsEquivalentTo(
			[$"{_token} me=#{_commands.Number} enactor=#{_actor.DbRef.Number} caller=#{_actor.DbRef.Number} arg=word"]);
	}

	[Test]
	public async Task TypedMatchRunsInPlace()
	{
		// A command typed at the connection is QUEUE_INPLACE: its body has run by the time the command returns.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} typed");

		await Run(_token);

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} typed"]);
	}

	// The next four compare a register set while the command line is evaluated for matching: in place the
	// body shares it, queued it starts empty. PennMUSH 1.8.8 (80a1d5b) typed at a connection, with
	// `&CMD obj=$+hi:@pemit %#=body me=%! enactor=%# caller=%@ q0=[r(0)]`:
	//   +hi[setq(0,typed)]                 -> body me=#3 enactor=#1 caller=#1 q0=typed
	//   teach +hi[setq(0,typed)]           -> One types --> +hi[setq(0,typed)]
	//                                         body me=#3 enactor=#1 caller=#1 q0=
	//   with obj=+hi[setq(0,typed)]        -> body me=#3 enactor=#1 caller=#1 q0=
	//   teach &FOO me=[add(1,2)]           -> FOO holds [add(1,2)]
	private const string RegisterProbe = "@pemit %#={0} body me=%! enactor=%# caller=%@ q0=[r(0)]";

	private List<string> HeardBodies() => Heard().Where(message => message.StartsWith($"{_token} body", StringComparison.Ordinal)).ToList();

	[Test]
	public async Task TypedMatchSharesTheRegistersOfItsLine()
	{
		await Run($"&CMD {_commands}=${_token}:{string.Format(RegisterProbe, _token)}");

		await Run($"{_token}[setq(0,typed)]");

		await Assert.That(HeardBodies()).IsEquivalentTo(
			[$"{_token} body me=#{_commands.Number} enactor=#{_actor.DbRef.Number} caller=#{_actor.DbRef.Number} q0=typed"]);
	}

	[Test]
	public async Task TaughtMatchIsQueued()
	{
		// do_teach (src/speech.c) runs the lesson without QUEUE_SOCKET, so a $-command it reaches is queued.
		await Run($"&CMD {_commands}=${_token}:{string.Format(RegisterProbe, _token)}");

		await Run($"teach {_token}[setq(0,typed)]");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardBodies()).IsEquivalentTo(
			[$"{_token} body me=#{_commands.Number} enactor=#{_actor.DbRef.Number} caller=#{_actor.DbRef.Number} q0="]);
	}

	[Test]
	public async Task TaughtBuiltinStillStoresTheValueUnevaluated()
	{
		// The lesson keeps QUEUE_NOLIST, so & stores its value as typed.
		await Run("teach &TAUGHT me=[add(1,2)]");

		await Assert.That(await EvalAs(_actor.DbRef, "get(me/TAUGHT)")).IsEqualTo("[add(1,2)]");
	}

	[Test]
	public async Task WithMatchIsQueuedWithTheTyperAsEnactorAndCaller()
	{
		// cmd_with (src/game.c) matches with QUEUE_DEFAULT, never in place, and the typer is the matcher.
		await Run($"&CMD {_commands}=${_token}:{string.Format(RegisterProbe, _token)}");

		await Run($"with {_commands}={_token}[setq(0,typed)]");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardBodies()).IsEquivalentTo(
			[$"{_token} body me=#{_commands.Number} enactor=#{_actor.DbRef.Number} caller=#{_actor.DbRef.Number} q0="]);
	}

	[Test]
	public async Task WithMatchesOnlyTheTargetsCommands()
	{
		// PennMUSH 1.8.8 (80a1d5b): with another object nearby holding the same $+hi, `with Obj=+hi` runs
		// only Obj's body, and a built-in is not a match: `with Obj=@pemit me=builtin` -> No matching command.
		var other = await CreateThing("QueuedCmdOther");
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body me=%!");
		await Run($"&CMD {other}=${_token}:@pemit %#={_token} other me=%!");

		await Run($"with {_commands}={_token}");
		await Run($"with {_commands}=@pemit me={_token} builtin");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} body me=#{_commands.Number}"]);
		await Assert.That(Notifications.For(_actor.DbRef)).Contains("No matching command.");
	}

	/// <summary>A thing God owns, dropped into <paramref name="room"/>: the actor neither owns nor controls it.</summary>
	private async Task<DBRef> CreateForeignThing(string prefix, string room)
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		await Cmd($"@set {thing}=!no_command");
		await Cmd($"@tel {thing}={room}");
		return thing;
	}

	private List<string> HeardSince(int start) => Notifications.For(_actor.DbRef).Skip(start).ToList();

	[Test]
	public async Task WithRunsANearbyObjectThePlayerDoesNotControl()
	{
		// cmd_with (src/game.c:1398) admits anything nearby, not only what the player controls. PennMUSH
		// 1.8.8 (80a1d5b): a mortal's `with Widget=wcmd` on God's !no_command Widget in the same room
		// prints nothing and queues Widget's $wcmd.
		var foreign = await CreateForeignThing("QueuedCmdForeign", _room);
		await Cmd($"&CMD {foreign}=${_token}:@pemit %#={_token} body me=%!");
		var start = Notifications.CountFor(_actor.DbRef);

		await Run($"with {foreign}={_token}");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardSince(start)).IsEquivalentTo([$"{_token} body me=#{foreign.Number}"]);
	}

	[Test]
	public async Task WithTriesARoomItself()
	{
		// PennMUSH 1.8.8 (80a1d5b): a mortal's `with #0=wcmd` from inside #0 -> No matching command.
		// The room is a legal target; it simply has no $wcmd.
		var start = Notifications.CountFor(_actor.DbRef);

		await Run($"with here={_token}");

		await Assert.That(HeardSince(start)).IsEquivalentTo(["No matching command."]);
	}

	[Test]
	public async Task WithRoomRunsTheRoomsContents()
	{
		// /ROOM tries the contents of the room as a master room would be tried (src/game.c:1419-1427).
		// PennMUSH 1.8.8 (80a1d5b): `with/room here=wcmd` with a $wcmd thing in the room prints nothing
		// and queues it; `with/room here=wnope` -> No matching command.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body me=%!");
		var start = Notifications.CountFor(_actor.DbRef);

		await Run($"with/room here={_token}");
		await Run($"with/room here={_token}nope");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardSince(start))
			.IsEquivalentTo([$"{_token} body me=#{_commands.Number}", "No matching command."]);
	}

	[Test]
	public async Task WithRoomOnSomethingThatIsNotARoomMakesRoom()
	{
		// PennMUSH 1.8.8 (80a1d5b): `with/room Widget=wcmd` on a thing, and `with/room me=wcmd`, both
		// print `Make room! Make room!` and run nothing.
		await Run($"&CMD {_commands}=${_token}:@pemit %#={_token} body me=%!");
		var start = Notifications.CountFor(_actor.DbRef);

		await Run($"with/room {_commands}={_token}");
		await Run($"with/room me={_token}");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardSince(start)).IsEquivalentTo(["Make room! Make room!", "Make room! Make room!"]);
	}

	[Test]
	[Arguments("with")]
	[Arguments("with/room")]
	public async Task WithOutOfReachIsRefused(string command)
	{
		// PennMUSH 1.8.8 (80a1d5b): a mortal naming God's thing in another room by dbref -> I don't see
		// that here., for both forms (src/game.c:1398-1411).
		var elsewhere = await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("QueuedCmdFar")}");
		var far = await CreateForeignThing("QueuedCmdFarThing", elsewhere);
		await Cmd($"&CMD {far}=${_token}:@pemit %#={_token} body me=%!");
		var start = Notifications.CountFor(_actor.DbRef);

		await Run($"{command} {far}={_token}");
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(HeardSince(start)).IsEquivalentTo(["I don't see that here."]);
	}

	[Test]
	[Arguments("with")]
	[Arguments("with/room")]
	public async Task WithMissIsReportedToThePlayerOnce(string command)
	{
		// #1237: /ROOM put the room in the executor position of the locate, so the miss went to the room
		// and the player got a second message of SharpMUSH's own. cmd_with matches as the player for
		// both forms and adds nothing to the match error. PennMUSH 1.8.8 (80a1d5b): `with WNoSuch=wcmd`
		// and `with/room WNoSuch=wcmd` -> I can't see that here.
		var roomRef = DBRef.Parse(_room);
		var start = Notifications.CountFor(_actor.DbRef);
		var roomStart = Notifications.CountFor(roomRef);

		await Run($"{command} {_token}nosuchthing={_token}");

		await Assert.That(HeardSince(start)).IsEquivalentTo(["I can't see that here."]);
		await Assert.That(Notifications.For(roomRef).Skip(roomStart)).IsEmpty();
	}

	// The `]` cases below were checked against a disposable PennMUSH 1.8.8 world (80a1d5b9) with
	// `&CMD Cmd=$+hi *:@pemit %#=got:[%0]|raw:%0` on a !no_command thing. command_parse strips the
	// NOEVAL_TOKEN (src/command.c:1160-1166) and goes on to $-command matching with the line unevaluated:
	//   +hi [add(1,2)]      -> got:3|raw:3
	//   ]+hi [add(1,2)]     -> got:[add(1,2)]|raw:[add(1,2)]
	//   ]+hi %n             -> got:%n|raw:%n
	//   ]+hi   spaced   out -> got:  spaced   out|raw:  spaced   out
	//   ] +hi x             -> got:x|raw:x
	//   &T me=]+hi [add(1,2)];@pemit me=after, @trigger me/T -> after, then got:[add(1,2)]|raw:[add(1,2)]
	[Test]
	[Arguments("]{0} [add(1,2)]", "{0} got:[add(1,2)]|raw:[add(1,2)]")]
	[Arguments("]{0} %n", "{0} got:%n|raw:%n")]
	[Arguments("]{0}   spaced   out", "{0} got:  spaced   out|raw:  spaced   out")]
	[Arguments("] {0} x", "{0} got:x|raw:x")]
	[Arguments("{0} [add(1,2)]", "{0} got:3|raw:3")]
	public async Task TypedNoEvalTokenMatchesADollarCommandWithoutEvaluating(string typed, string expected)
	{
		await Run($"&CMD {_commands}=${_token} *:@pemit %#={_token} got:[%0]|raw:%0");

		await Run(string.Format(typed, _token));

		await Assert.That(Heard()).IsEquivalentTo([string.Format(expected, _token)]);
	}

	[Test]
	public async Task SoftcodeNoEvalTokenMatchStillQueues()
	{
		await Run($"&CMD {_commands}=${_token} *:@pemit %#={_token} got:[%0]");

		await RunQueued($"]{_token} [add(1,2)];@pemit me={_token} after");

		await Assert.That(Heard()).IsEquivalentTo([$"{_token} after", $"{_token} got:[add(1,2)]"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task HaltStopsAMatchThatKeepsQueueingItself()
	{
		// PennMUSH: `$+loop:@pemit *One=tick;@force Pup=+loop` ticks without end, each match a new queue
		// entry with its own q-registers and budget, until `@halt` on the command object drops the entry
		// waiting to run. Nested in place, the register would grow by one each time round.
		var puppet = await CreateThing("QueuedCmdPup");
		await Run($"&LOOP {_commands}=${_token}:think setq(0,r(0)x);@pemit {_actor.DbRef}={_token} tick [strlen(r(0))];@force {puppet}={_token}");

		await Mediator.Send(new AdmitCommandListRequest(
			MString.Plain($"@force {puppet}={_token}"),
			WebAppFactoryArg.CommandParserFor(_actor.DbRef, _actor.Handle).CurrentState,
			new DbRefAttribute(_actor.DbRef, ["QUEUED_TEST"]),
			-1));
		await Assert.That(() => Heard().Count).WaitsFor(count => count.IsGreaterThanOrEqualTo(20),
			timeout: TimeSpan.FromSeconds(30), pollingInterval: TimeSpan.FromMilliseconds(50));

		await RunQueued($"@halt {_commands}");
		var ticks = Heard();
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(ticks.Distinct()).IsEquivalentTo([$"{_token} tick 1"]);
		await Assert.That(Heard().Count).IsEqualTo(ticks.Count);
	}
}
