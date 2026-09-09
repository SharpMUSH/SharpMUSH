using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@set</c> and <c>set()</c> are one routine in PennMUSH: <c>cmd_set</c> is
/// <c>do_set(executor, arg_left, arg_right)</c> (<c>src/cmds.c:1410</c>) and <c>fun_set</c> is
/// <c>do_set(executor, args[0], args[1])</c> (<c>src/fundb.c:2251</c>), which is what
/// <c>help set()</c> means by "This function is equivalent to @set". Anything the two spellings
/// do differently here is a divergence from Penn by construction, so these tests assert the two
/// against each other rather than against a hand-written expectation.
/// </summary>
[NotInParallel]
public class SetDispatchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<bool> HasFlag(DBRef who, string flag)
		=> await (await Mediator.Send(new GetObjectNodeQuery(who))).Known.HasFlag(flag);

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>
	/// <c>do_set</c>'s flag branch is <c>do { f = split_token(&amp;p, ' '); … set_flag(…) } while (p)</c>
	/// (<c>src/set.c:658-673</c>) — one <c>set_flag</c> per space-separated token, reached identically
	/// from the command and the function. Passing the whole right-hand side as a single flag name
	/// makes <c>set(obj, dark opaque)</c> look for a flag literally called "dark opaque".
	/// </summary>
	[Test]
	public async ValueTask SetFunction_FlagList_SetsEveryFlagInTheList()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetFnFlagList");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think set({obj}, DARK OPAQUE)"));

		await Assert.That(await HasFlag(obj, "DARK")).IsTrue()
			.Because("do_set splits its flag argument on spaces and calls set_flag once per token");
		await Assert.That(await HasFlag(obj, "OPAQUE")).IsTrue()
			.Because("the second token of the list must be applied too");
	}

	/// <summary>
	/// The parity assertion: the same flag list through both spellings has to leave the object in
	/// the same state, because Penn reaches the same <c>do_set</c> either way.
	/// </summary>
	[Test]
	public async ValueTask SetFunctionAndSetCommand_LeaveTheSameFlagState()
	{
		var viaCommand = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetParityCmd");
		var viaFunction = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetParityFn");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {viaCommand}=DARK OPAQUE"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think set({viaFunction}, DARK OPAQUE)"));

		await Assert.That(await HasFlag(viaFunction, "DARK")).IsEqualTo(await HasFlag(viaCommand, "DARK"))
			.Because("set() is @set: the two spellings must agree on every flag in the list");
		await Assert.That(await HasFlag(viaFunction, "OPAQUE")).IsEqualTo(await HasFlag(viaCommand, "OPAQUE"))
			.Because("set() is @set: the two spellings must agree on every flag in the list");
	}

	/// <summary>
	/// The unset half of the same split: <c>!</c> is stripped per token
	/// (<c>src/set.c:666-670</c>), so a mixed list has to reach <c>set_flag</c> with the right
	/// negate value for each token independently.
	/// </summary>
	[Test]
	public async ValueTask SetFunction_MixedFlagList_SetsAndUnsetsPerToken()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetFnMixed");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}=DARK"));
		await Assert.That(await HasFlag(obj, "DARK")).IsTrue().Because("precondition for the unset half");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think set({obj}, !DARK OPAQUE)"));

		await Assert.That(await HasFlag(obj, "DARK")).IsFalse()
			.Because("the !-prefixed token unsets its own flag");
		await Assert.That(await HasFlag(obj, "OPAQUE")).IsTrue()
			.Because("the un-prefixed token in the same list still sets");
	}

	/// <summary>
	/// <c>set_flag</c> reports the change to <c>player</c> unconditionally
	/// (<c>src/flags.c:1855-1864,1914-1923</c>), and <c>fun_set</c> passes the same <c>player</c>
	/// through <c>do_set</c> — there is no quiet variant on the function side. So a side-effect
	/// <c>set()</c> notifies exactly as <c>@set</c> does.
	/// </summary>
	[Test]
	public async ValueTask SetFunction_NotifiesTheExecutorLikeSetCommand()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SetNotify");
		var viaCommand = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetNotifyCmd");
		var viaFunction = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetNotifyFn");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {viaCommand}={owner.DbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {viaFunction}={owner.DbRef}"));

		var commandName = (await Mediator.Send(new GetObjectNodeQuery(viaCommand))).Known.Object().Name;
		var functionName = (await Mediator.Send(new GetObjectNodeQuery(viaFunction))).Known.Object().Name;

		var commandMessages = await MessagesWhile(owner.DbRef, () =>
			Parser.CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"@set {viaCommand}=DARK")).AsTask());

		var functionMessages = await MessagesWhile(owner.DbRef, () =>
			Parser.CommandParse(owner.Handle, ConnectionService,
				MarkupText.Plain($"think set({viaFunction}, DARK)")).AsTask());

		await Assert.That(commandMessages).Contains($"{commandName} - DARK set.")
			.Because("precondition: @set reports the flag it set");
		await Assert.That(functionMessages).Contains($"{functionName} - DARK set.")
			.Because("fun_set is do_set with the same player, so it reports the same line");
	}

	/// <summary>
	/// <c>do_set</c> resolves its target with <c>match_controlled(player, name)</c>
	/// (<c>src/set.c:635</c>) where <c>player</c> is the executor both <c>cmd_set</c> and
	/// <c>fun_set</c> hand it — the same object the permission check then uses. Locating as the
	/// ENACTOR instead means a forced object searches the forcer's surroundings, reaching objects
	/// it cannot see.
	/// </summary>
	[Test]
	public async ValueTask SetCommand_LocatesItsTargetAsTheExecutorNotTheEnactor()
	{
		var room = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("SetActorRoom")}"));
		var roomDb = DBRef.Parse(room.Message!.ToPlainText());

		var agent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetActorAgent");
		var beacon = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetActorBeacon");
		var beaconName = (await Mediator.Send(new GetObjectNodeQuery(beacon))).Known.Object().Name;

		// The agent goes elsewhere; the beacon stays with God, who does the forcing. The two are
		// therefore in different places, which is the only thing separating "looked for as the
		// executor" from "looked for as the enactor".
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {agent}={roomDb}"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@force {agent}=@set {beaconName}=DARK"));

		await Assert.That(await HasFlag(beacon, "DARK")).IsFalse()
			.Because("match_controlled runs as the executor, and the forced agent cannot see the beacon");

		// Control: with the beacon in the same room the executor CAN see it, so the refusal above
		// is about the search origin and not about @set failing under @force generally.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport {beacon}={roomDb}"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@force {agent}=@set {beaconName}=DARK"));

		await Assert.That(await HasFlag(beacon, "DARK")).IsTrue()
			.Because("the same command succeeds once the target is where the executor can see it");
	}

	/// <summary>
	/// The same rule read through <c>me</c>: <c>match_controlled(executor, "me")</c> is the
	/// executor, so a forced <c>@set me=…</c> flags the forced object rather than whoever forced it.
	/// </summary>
	[Test]
	public async ValueTask SetCommand_MeUnderForce_IsTheForcedObject()
	{
		var forcer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SetMeForcer");
		var agent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetMeAgent");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {agent}={forcer.DbRef}"));
		await Parser.CommandParse(forcer.Handle, ConnectionService, MarkupText.Plain($"@force {agent}=@set me=DARK"));

		await Assert.That(await HasFlag(agent, "DARK")).IsTrue()
			.Because("\"me\" in a forced command is the forced object, which is do_set's player");
		await Assert.That(await HasFlag(forcer.DbRef, "DARK")).IsFalse()
			.Because("the enactor is not do_set's player and must not be the one flagged");
	}
}
