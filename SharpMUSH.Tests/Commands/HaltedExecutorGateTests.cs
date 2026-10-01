using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH refuses a halted executor's command in <c>process_command</c> (<c>src/game.c:1181</c>):
///
/// <code>
/// Halted(executor) &amp;&amp; (!IsPlayer(executor) || !(queue_entry-&gt;queue_type &amp; QUEUE_SOCKET))
/// </code>
///
/// so a halted <em>player's</em> typed line still runs while everything the queue carries for them is
/// refused, and the executor's owner is told <c>Attempt to execute command by halted object #N</c>
/// once per refused command. That message is the only place the refusal is audible: the queue itself
/// drops a halted object's entry without a word (<c>insert_que</c>, <c>src/cque.c:530</c>) and admits
/// a halted player's, so this gate is where a player's halt is actually enforced. #1006 items 1+2.
///
/// <para>Observed on a live PennMUSH 1.8.8 (<c>80a1d5b9</c>) as <c>#1</c> with a second connection as
/// the halted player <c>HPlayer(#6)</c>, <c>@set</c> only:</para>
/// <list type="bullet">
/// <item>typed by the halted player — <c>&amp;TYPEDATTR me=typedok</c> answers
/// <c>HPlayer/TYPEDATTR - Set.</c> and <c>get(HPlayer/TYPEDATTR)</c> is <c>typedok</c>.</item>
/// <item>queued for the halted player — <c>@force HPlayer=&amp;QUEUED me=queuedran</c> prints
/// <c>Attempt to execute command by halted object #6</c> in HPlayer's window (HPlayer owns itself),
/// and <c>get(HPlayer/QUEUED)</c> stays empty.</item>
/// </list>
///
/// <para><see cref="QueuedHaltTests"/> pins the admission half that #1086 owns — that the queue
/// exempts players, so the entry is admitted rather than dropped. This class pins what happens once
/// that admitted entry starts.</para>
///
/// <para>Every refused command is waited for by its refusal, which names the test's own object, rather
/// than by draining the session-wide queue.</para>
/// </summary>
public class HaltedExecutorGateTests : ServerTestBase
{
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	/// <summary>The dbref the database stores, which is the one a notification is keyed on.</summary>
	private async Task<DBRef> Canonical(DBRef reference) =>
		(await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>().Object().DBRef;

	/// <summary>Queues <paramref name="list"/> as <paramref name="executor"/>, held until released.</summary>
	private ValueTask<QueueAdmissionResult> Wait(DBRef executor, string list) =>
		Scheduler.AdmitCommandList(MString.Plain(list), ParserState.Empty with { Executor = executor }, TimeSpan.FromHours(1));

	private static long RequirePid(QueueAdmissionResult admission) =>
		admission.Pid ?? throw new InvalidOperationException($"Fixture admission rejected: {admission.Reason}");

	/// <summary>Brings <paramref name="pid"/> due and waits until <paramref name="told"/> hears <paramref name="executor"/> refused.</summary>
	private async Task RunUntilRefused(long pid, DBRef told, int start, DBRef executor)
	{
		await Scheduler.ReleaseScheduledWork(pid);
		await Notifications.WaitForAsync(told, Refusal(executor), startIndex: start);
	}

	private static string Refusal(DBRef executor) =>
		$"Attempt to execute command by halted object #{executor.Number}";

	/// <summary>
	/// The gate reads the flag per command, not per entry: an action list that halts its own executor
	/// part-way through loses the rest of itself, because <c>process_command</c> runs for every command
	/// in the list and only the first ran unhalted. Nothing else in SharpMUSH catches this — admission
	/// saw an unhalted object and the dequeue re-check ran before the first command.
	/// </summary>
	[Test]
	public async Task HaltSetPartWayThroughAListRefusesTheRestOfItAndTellsTheOwner()
	{
		var created = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HaltGateMidList");
		var thing = await Canonical(created);
		var owner = await Canonical(new DBRef(1));
		var start = Notifications.CountFor(owner);

		var job = await Wait(thing, $"@set {thing}=HALT;&RAN {thing}=yes");
		await Assert.That(job.Accepted).IsTrue().Because("the object was not halted when the list was queued");

		await RunUntilRefused(RequirePid(job), owner, start, thing);

		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo("")
			.Because("the second command's executor was halted by the first");
	}

	/// <summary>
	/// The whole of the player half in one run: the queued body is refused and the owner told, and the
	/// same player's typed line runs regardless, because that one carries <c>QUEUE_SOCKET</c>.
	/// </summary>
	[Test]
	public async Task HaltedPlayersQueuedCommandIsRefusedWhileTheirTypedOneRuns()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HaltGatePlayer");
		var who = await Canonical(player.DbRef);
		try
		{
			await Cmd($"@set {who}=HALT");
			var start = Notifications.CountFor(who);

			var job = await Wait(who, $"&QUEUED {who}=queuedran");
			await Assert.That(job.Accepted).IsTrue().Because("insert_que exempts players (src/cque.c:530)");

			// A player owns itself, so the refusal lands in its own window.
			await RunUntilRefused(RequirePid(job), who, start, who);

			await Assert.That(await Eval($"get({who}/QUEUED)")).IsEqualTo("");

			await CmdAs(who, player.Handle, $"&TYPED {who}=typedok");

			await Assert.That(await Eval($"get({who}/TYPED)")).IsEqualTo("typedok")
				.Because("the typed line is QUEUE_SOCKET, which the gate exempts for a player");
		}
		finally
		{
			await Cmd($"@set {who}=!HALT");
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>
	/// The gate runs before the command name is looked up in anything, so a <c>SOCKET</c> command is
	/// refused like the rest. A queue entry keeps the handle it was started from, and the socket
	/// branch tests only that handle, so a queued <c>WHO</c> would otherwise be answered for an
	/// executor <c>process_command</c> refuses. The same <c>WHO</c> typed by the halted player still
	/// answers: <c>do_command</c> (<c>src/bsd.c:4155-4269</c>) reaches the connection-level commands
	/// before <c>process_command</c> is called at all.
	/// </summary>
	[Test]
	public async Task HaltedPlayersQueuedSocketCommandIsRefusedWhileTheirTypedOneAnswers()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HaltGateSocket");
		var who = await Canonical(player.DbRef);
		try
		{
			await Cmd($"@set {who}=HALT");
			var start = Notifications.CountFor(who);
			var listings = Notifications.CountForHandle(player.Handle);

			// Queued from the player's own connection, so the entry keeps the handle — the state the
			// socket branch reads. CommandListParse clears DirectInput when the entry runs, which is
			// the whole of the difference between this and the typed WHO below.
			var typedState = WebAppFactoryArg.CommandParserFor(who, player.Handle).CurrentState;
			var job = await Scheduler.AdmitCommandList(MString.Plain("WHO"), typedState, TimeSpan.FromHours(1));
			await Assert.That(job.Accepted).IsTrue();
			await RunUntilRefused(RequirePid(job), who, start, who);

			// By the listing's header, not by emptiness: a logged-in handle also hears every @wall
			// another test makes, so "nothing reached this connection" is not a claim it can make.
			await Assert.That(Notifications.ForHandle(player.Handle).Skip(listings))
				.DoesNotContain(line => line.Contains("Player Name", StringComparison.Ordinal))
				.Because("the queued WHO was refused before the socket branch could answer it");

			await CmdAs(who, player.Handle, "WHO");

			await Assert.That(Notifications.ForHandle(player.Handle).Skip(listings))
				.Contains(line => line.Contains("Player Name", StringComparison.Ordinal))
				.Because("a typed WHO is answered before process_command is reached");
		}
		finally
		{
			await Cmd($"@set {who}=!HALT");
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>
	/// <c>do_teach</c> queues its lesson with neither <c>QUEUE_SOCKET</c> nor <c>QUEUE_INPLACE</c>
	/// (<c>src/speech.c:130-163</c>), so the lesson is not the typed line the gate exempts even though
	/// <c>TEACH</c> itself was typed. <see cref="ParserStateFlags.QueueMatches"/> is what carries that
	/// distinction through the redispatch.
	/// </summary>
	[Test]
	public async Task HaltedPlayersTypedTeachRunsButItsLessonDoesNot()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HaltGateTeach");
		var who = await Canonical(player.DbRef);
		try
		{
			await Cmd($"@set {who}=HALT");
			var start = Notifications.CountFor(who);

			await CmdAs(who, player.Handle, $"teach &TAUGHT {who}=lessonran");
			await Notifications.WaitForAsync(who, Refusal(who), startIndex: start);

			await Assert.That(await Eval($"get({who}/TAUGHT)")).IsEqualTo("")
				.Because("the lesson carries no QUEUE_SOCKET, so the gate refuses it");
			await Assert.That(Notifications.For(who).Skip(start).Any(m => m.Contains("types -->"))).IsTrue()
				.Because("TEACH itself was typed, so it ran and announced the lesson");
		}
		finally
		{
			await Cmd($"@set {who}=!HALT");
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>
	/// <c>queue_attribute_useatr</c> queues an action attribute without reading the object's flags, and
	/// <c>insert_que</c> then exempts a player from the halted drop, so a halted player's
	/// <c>@a</c>-attribute is queued and reaches the gate — which refuses it audibly. Dropping it at
	/// the triad instead would run nothing and say nothing, and would leave the runaway path unable to
	/// set the flag on a player at all (<c>src/cque.c:312</c>). #1006 item 2.
	/// </summary>
	[Test]
	public async Task HaltedPlayersActionAttributeIsQueuedAndRefusedRatherThanSilentlyDropped()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HaltGateAction");
		var who = await Canonical(player.DbRef);
		try
		{
			await Cmd($"&ADESCRIBE {who}=&LOOKED {who}=ran");
			await Cmd($"@set {who}=HALT");
			var start = Notifications.CountFor(who);

			await Cmd($"look {who}");
			// The action was queued, then refused by the gate.
			await Notifications.WaitForAsync(who, Refusal(who), startIndex: start);

			await Assert.That(await Eval($"get({who}/LOOKED)")).IsEqualTo("");
		}
		finally
		{
			await Cmd($"&ADESCRIBE {who}=");
			await Cmd($"@set {who}=!HALT");
			await ConnectionService.Disconnect(player.Handle);
		}
	}
}
