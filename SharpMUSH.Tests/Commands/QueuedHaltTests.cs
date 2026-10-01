using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH reads HALT twice for a non-player: once when the work is queued and once when it starts.
/// <c>insert_que</c> (<c>src/cque.c:530</c>) and <c>new_queue_actionlist_int</c> (<c>:621</c>) throw the
/// entry away before a PID is allocated, and <c>do_entry</c> (<c>:1136</c>, reached for every ordinary
/// entry through <c>do_top</c>, <c>:1069</c>) abandons the whole action list if the flag arrived after
/// queueing. So an already-halted object queues nothing, an object halted while its <c>@wait</c> is
/// pending loses it when it comes due, and an object unhalted before then keeps it. Players are exempt
/// from all three checks. Regression cover for #1086.
///
/// <para>Observed on a live PennMUSH 1.8.8 (<c>80a1d5b9</c>) as <c>#1</c>, <c>@set</c> only — never
/// <c>@halt</c>, so nothing is ever cancelled:</para>
/// <list type="bullet">
/// <item>halted while waiting — <c>@ps/all</c> still lists <c>(Pid: 2) [2]HObjA(#3Thn): &amp;RAN HObjA=yes</c>
/// after <c>@set HObjA=halt</c>; when it comes due <c>get(HObjA/RAN)</c> is empty, the queue is empty
/// again, and nothing is printed to the owner.</item>
/// <item>unhalted before due — <c>get(HObjB/RAN)</c> is <c>yes</c>.</item>
/// <item>already halted — <c>@force HObjC=@wait 3=...</c> leaves <c>@ps/all</c> completely empty; both
/// that and an undelayed <c>@force HObjC=&amp;NOW HObjC=immediate</c> set nothing.</item>
/// <item>halted player — <c>@ps/all</c> lists <c>(Pid: 6) [2]HPlayer(#6PehnA): &amp;RAN me=yes</c>, so the
/// queue checks did exempt it.</item>
/// </list>
///
/// <para>A halted <em>player's</em> queued body does not ultimately run either, but PennMUSH stops it
/// one layer further down, in <c>process_command</c> (<c>src/game.c:1181</c>), which refuses any
/// command whose executor is halted unless it came from a socket and tells the owner
/// <c>Attempt to execute command by halted object #6</c>. That gate is player HALT policy, belongs to
/// #1006 and is pinned by <see cref="HaltedExecutorGateTests"/>; these tests pin only what #1086 owns
/// — that the queue exempts players, so the entry is admitted and reaches execution rather than being
/// dropped as an object's would be.</para>
/// </summary>
public class QueuedHaltTests : ServerTestBase
{
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	/// <summary>A fresh thing, as the canonical dbref the scheduler stores.</summary>
	private async Task<DBRef> CreateThing(string prefix)
	{
		var created = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, prefix);
		return (await Mediator.Send(new GetObjectNodeQuery(created))).Expect<AnySharpObject>().Object().DBRef;
	}

	/// <summary>Queues <paramref name="list"/> as <paramref name="executor"/>, held until released — Penn's wait queue.</summary>
	private ValueTask<QueueAdmissionResult> Wait(DBRef executor, string list) =>
		Scheduler.AdmitCommandList(MString.Plain(list), ParserState.Empty with { Executor = executor }, TimeSpan.FromHours(1));

	private static long RequirePid(QueueAdmissionResult admission) =>
		admission.Pid ?? throw new InvalidOperationException($"Fixture admission rejected: {admission.Reason}");

	/// <summary>Brings <paramref name="pid"/> due and waits for it and anything it queued.</summary>
	private async Task Run(long pid)
	{
		await Scheduler.ReleaseScheduledWork(pid);
		await Scheduler.DrainImmediateQueueForTests();
	}

	[Test]
	public async Task WaitedBodyDoesNotRunWhenItsObjectIsHaltedBeforeItComesDue()
	{
		var thing = await CreateThing("QHaltWait");
		var job = await Wait(thing, $"&RAN {thing}=yes");
		await Assert.That(job.Accepted).IsTrue();

		// @set, not @halt: the flag alone must stop it, with nothing cancelled.
		await Cmd($"@set {thing}=HALT");
		await Run(RequirePid(job));

		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo("");
	}

	[Test]
	public async Task WaitedBodyRunsWhenItsObjectIsUnhaltedBeforeItComesDue()
	{
		var thing = await CreateThing("QHaltUnwait");
		var job = await Wait(thing, $"&RAN {thing}=yes");
		await Cmd($"@set {thing}=HALT");
		await Cmd($"@set {thing}=!HALT");

		await Run(RequirePid(job));

		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo("yes");
	}

	/// <summary>
	/// The entry is dropped, not cancelled: it leaves the queue and gives its slot back exactly as a
	/// completed one does, because <c>do_entry</c> refunds the cost and decrements the object's queue
	/// count (<c>src/cque.c:1131</c>) before it looks at the flag, and <c>do_top</c> frees it either way.
	/// </summary>
	[Test]
	public async Task DroppedEntryLeavesTheQueueWithoutBeingCancelled()
	{
		var thing = await CreateThing("QHaltSlot");
		var job = await Wait(thing, $"&RAN {thing}=yes");
		var pid = RequirePid(job);
		await Cmd($"@set {thing}=HALT");

		await Run(pid);

		await Assert.That(Scheduler.GetQueueEntry(pid)).IsNull();
		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo("");
	}

	/// <summary>Only the halted object's own work goes; a sibling entry is untouched.</summary>
	[Test]
	public async Task HaltOnOneObjectDoesNotDropAnotherObjectsQueuedBody()
	{
		var halted = await CreateThing("QHaltMine");
		var clear = await CreateThing("QHaltTheirs");
		var haltedJob = await Wait(halted, $"&RAN {halted}=yes");
		var clearJob = await Wait(clear, $"&RAN {clear}=yes");
		await Cmd($"@set {halted}=HALT");

		await Run(RequirePid(haltedJob));
		await Run(RequirePid(clearJob));

		await Assert.That(await Eval($"get({halted}/RAN)")).IsEqualTo("");
		await Assert.That(await Eval($"get({clear}/RAN)")).IsEqualTo("yes");
	}

	/// <summary>
	/// An already-halted non-player is refused at admission, so there is no PID to inspect, nothing in
	/// the queue and no owner's slot spent — PennMUSH's <c>@ps/all</c> stays empty.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AlreadyHaltedObjectQueuesNothing(bool delayed)
	{
		var thing = await CreateThing(delayed ? "QHaltAdmitWait" : "QHaltAdmitNow");
		await Cmd($"@set {thing}=HALT");

		var job = delayed
			? await Wait(thing, $"&RAN {thing}=yes")
			: await Scheduler.AdmitCommandList(MString.Plain($"&RAN {thing}=yes"), ParserState.Empty with { Executor = thing });

		await Assert.That(job.Accepted).IsFalse();
		await Assert.That(job.Reason).IsEqualTo(QueueRejectionReason.Halted);
		await Assert.That(job.Pid).IsNull();
		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo("");
	}

	/// <summary>
	/// PennMUSH's queue checks exempt players, so a halted player's body is admitted and keeps its PID
	/// where an object's would never have been queued. What happens once it starts is #1006's
	/// (<c>process_command</c>, <c>src/game.c:1181</c>).
	/// </summary>
	[Test]
	public async Task HaltedPlayersQueuedBodyIsStillAdmitted()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "QHaltPlayer");
		try
		{
			await Cmd($"@set {player.DbRef}=HALT");

			var job = await Wait(player.DbRef, "think halted player");

			await Assert.That(job.Accepted).IsTrue();
			await Assert.That(Scheduler.GetQueueEntry(RequirePid(job))).IsNotNull();
			await Scheduler.HaltByPid(RequirePid(job));
		}
		finally
		{
			await Cmd($"@set {player.DbRef}=!HALT");
			await ConnectionService.Disconnect(player.Handle);
		}
	}
}
