using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// An object that keeps growing its own backlog is halted and its owner told, and the queue every
/// other player shares keeps working. PennMUSH <c>queue_limit</c> / <c>pay_queue</c>
/// (<c>src/cque.c:226,303</c>).
/// </summary>
/// <remarks>
/// <para>
/// The quota counts an owner's PENDING entries, exactly as PennMUSH's <c>add_to</c> pair does, so
/// what it catches is fan-out — a cycle where each dequeue queues more than one successor and the
/// backlog grows. A one-deep cycle (two objects whose <c>@adescribe</c> looks at the other) sits at
/// one pending entry forever and never trips the limit; that is true of PennMUSH too, and is a
/// livelock that spins without growing rather than a queue exhaustion. The runaway here therefore
/// looks at itself TWICE per run.
/// </para>
/// <para>
/// The runaway is owned by a fresh, non-wizard player rather than by God. The quota is per owner
/// and <see cref="ServerWebAppFactory"/> is shared for the whole test session, so charging a
/// runaway to God would spend the allowance of every other test that queues work as God. A
/// non-wizard owner also fixes the quota at the configured limit, since wizards and holders of the
/// <c>Queue</c> power get the database size on top of it.
/// </para>
/// <para>
/// <c>[NotInParallel]</c>: these tests deliberately park a full quota's worth of entries on the
/// immediate queue, and <see cref="ServerWebAppFactory"/> makes that queue — and
/// <c>DrainImmediateQueueForTests</c>, which waits on the whole of it — session-wide. A test that
/// drains with the default five-second timeout while this backlog is being worked through times out
/// and throws, so this class is serialised against the other <c>[NotInParallel]</c> classes.
/// </para>
/// </remarks>
[NotInParallel]
public class QueueQuotaTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	/// <summary>
	/// The drain has to outlast a full quota's worth of queued looks plus the backlog they built,
	/// and still fail rather than hang if the halt never lands.
	/// </summary>
	private static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(2);

	/// <summary>
	/// Builds a thing owned by <paramref name="owner"/> whose <c>@adescribe</c> looks at itself
	/// twice, so every entry that runs queues two more.
	/// </summary>
	private async Task<DBRef> CreateRunaway(DBRef owner, string prefix)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {thing}={owner}"));

		// @chown without /preserve sets HALT on what it moves (BuildingCommands.cs:297, PennMUSH
		// do_chown), and the look path refuses to queue an action attribute on a halted object
		// (GeneralCommands.cs:759). Left on, the runaway never starts and the HALT the test asserts on
		// is the one @chown set rather than the one the quota set.
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}=!HALT"));

		// Assert the clear took. Without this, hasflag(runaway,HALT) at the end of the test has two
		// possible authors — the quota and @chown — and the test passes on either.
		var startsUnhalted = await GodParser.FunctionParse(MarkupText.Plain($"[hasflag({thing},HALT)]"));
		await Assert.That(startsUnhalted!.Message!.ToPlainText().Trim()).IsEqualTo("0")
			.Because("the runaway has to start able to run, or the HALT the test asserts on is @chown's");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {thing}=look me;look me"));

		return thing;
	}

	/// <summary>
	/// Parks the single queue consumer on one entry that belongs to nobody, so that entries admitted
	/// after it stay pending and a test can look at the queue while it is loaded. The returned source
	/// has to be completed, in a <c>finally</c>, or the session-shared queue stops for good.
	/// </summary>
	private async Task<TaskCompletionSource> ParkTheConsumer()
	{
		var gate = new TaskCompletionSource();

		await Scheduler.EnqueueWork(
			async () =>
			{
				await gate.Task;
				return null;
			},
			"queue-quota-gate", TaskScheduler.EnqueueGroup);

		return gate;
	}

	/// <summary>
	/// Sets the runaway going and waits for the queue to fall quiet, clearing the action attribute
	/// afterwards whatever happens: an object still spinning when the test ends would eat the
	/// session-shared queue for every test that follows.
	/// </summary>
	private async Task RunToQuiet(DBRef runaway)
	{
		try
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"look {runaway}"));
			await Scheduler.DrainImmediateQueueForTests(DrainTimeout);
		}
		finally
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {runaway}="));
		}
	}

	[Test]
	public async ValueTask AFanOutActionCycleHaltsTheOffenderRatherThanRunningForever()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "RunawayOwner");
		var runaway = await CreateRunaway(owner, "Runaway");

		await RunToQuiet(runaway);

		var halted = await GodParser.FunctionParse(MarkupText.Plain($"[hasflag({runaway},HALT)]"));

		await Assert.That(halted!.Message!.ToPlainText().Trim()).IsEqualTo("1")
			.Because("an object that outruns its owner's queue quota is halted, as pay_queue does");
	}

	[Test]
	public async ValueTask AnUnrelatedOwnersQueuedWorkStillRunsAfterARunaway()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "HogOwner");
		var runaway = await CreateRunaway(owner, "Hog");

		await RunToQuiet(runaway);

		// The witness is God's, so it draws on a different owner's allowance entirely.
		var witness = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "Witness");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {witness}=&MARK me=set"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"look {witness}"));
		await Scheduler.DrainImmediateQueueForTests(DrainTimeout);

		var mark = await GodParser.FunctionParse(MarkupText.Plain($"[get({witness}/MARK)]"));

		await Assert.That(mark!.Message!.ToPlainText().Trim()).IsEqualTo("set");
	}

	/// <summary>
	/// A player's own typed line is charged to nobody, so their objects filling the owner quota
	/// cannot make the player the offender. PennMUSH's <c>run_user_input</c>
	/// (<c>src/cque.c:1076-1088</c>) builds its entry and calls <c>do_entry</c> directly, never
	/// reaching <c>insert_que</c> or <c>pay_queue</c>. The HALT flag stays off for the same reason
	/// Penn's two halted gates test <c>!IsPlayer(executor)</c> first (<c>src/cque.c:530</c>,
	/// <c>:1136</c>): a player who cannot type is not a quota outcome anyone wants.
	/// </summary>
	[Test]
	public async ValueTask APlayerCanStillTypeWhenTheirObjectsHaveFilledTheQuota()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "TypistOwner");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "Filler");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {thing}={player}"));

		var limit = (int)WebAppFactoryArg.Services
			.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>().CurrentValue.Limit.PlayerQueueLimit;

		var gate = await ParkTheConsumer();

		try
		{
			// Exactly the limit: the last of these is still admitted, so nothing has been halted yet and
			// the next admission charged to this owner is the one that would trip.
			for (var i = 0; i < limit; i++)
			{
				await Scheduler.EnqueueWork(() => ValueTask.FromResult<CallState?>(null),
					$"queue-quota-filler-{i}", TaskScheduler.EnqueueGroup, thing);
			}

			var typed = GodParser.CurrentState with
			{
				Executor = player,
				Enactor = player,
				Caller = player,
				Handle = 1,
				ConnectionSessionId = null
			};

			await Scheduler.WriteUserCommand(1, MarkupText.Plain($"&TYPED {thing}=ran"), typed);
		}
		finally
		{
			gate.SetResult();
		}

		await Scheduler.DrainImmediateQueueForTests(DrainTimeout);

		var halted = await GodParser.FunctionParse(MarkupText.Plain($"[hasflag({player},HALT)]"));
		await Assert.That(halted!.Message!.ToPlainText().Trim()).IsEqualTo("0")
			.Because("a player is never the runaway, and the flag would leave them unable to act");

		var ran = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/TYPED)]"));
		await Assert.That(ran!.Message!.ToPlainText().Trim()).IsEqualTo("ran")
			.Because("direct input is admitted whatever the owner's pending count is");
	}

	/// <summary>
	/// A semaphore released by <c>@notify</c> is ordinary queued work belonging to the object that
	/// waited. PennMUSH charges it to that executor when <c>wait_que</c> builds it
	/// (<c>src/cque.c:904</c>) and <c>dequeue_semaphores</c> moves the same entry onto the run queue
	/// with its executor intact (<c>src/cque.c:1379-1427</c>) — so it counts against the owner's
	/// quota and answers to <c>@halt</c> and <c>@ps</c> like anything else. Released without an
	/// executor it would be charged to nobody, and a <c>@notify</c>-driven cycle would walk straight
	/// past the quota that is supposed to stop it.
	/// </summary>
	[Test]
	public async ValueTask SemaphoreWorkIsChargedToTheObjectThatWaitedForIt()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "SemaphoreCharge");

		var waiting = GodParser.CurrentState with
		{
			Executor = thing,
			Enactor = thing,
			Caller = thing
		};

		var gate = await ParkTheConsumer();
		var pids = new List<long>();

		try
		{
			// The timed form is the one SemaphoreTask releases; @notify releases through
			// TaskScheduler.Notify, which threads the executor itself.
			await Scheduler.WriteCommandList(
				MarkupText.Plain($"&WOKE {thing}=yes"),
				waiting,
				new DbRefAttribute(thing, ["SEM"]),
				oldValue: 0,
				TimeSpan.FromMilliseconds(50));

			// The consumer is parked, so the entry SemaphoreTask releases stays pending and countable.
			// GetEnqueueTasks is exactly what @halt <object> and @ps ask about.
			var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

			while (pids.Count == 0 && DateTimeOffset.UtcNow < deadline)
			{
				pids = await Scheduler.GetEnqueueTasks(thing).ToListAsync();

				if (pids.Count == 0)
				{
					await Task.Delay(TimeSpan.FromMilliseconds(10));
				}
			}
		}
		finally
		{
			gate.SetResult();
		}

		await Assert.That(pids).IsNotEmpty()
			.Because("a released semaphore is the waiting object's queued work, not nobody's");

		await Scheduler.DrainImmediateQueueForTests(DrainTimeout);

		var woke = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/WOKE)]"));
		await Assert.That(woke!.Message!.ToPlainText().Trim()).IsEqualTo("yes")
			.Because("charging the work must not stop it running");
	}

	/// <summary>
	/// <c>@halt &lt;pid&gt;</c> takes the entry out of the pending set before it cancels it, so the
	/// entry it reports having halted is one it actually owned. A second call finds nothing.
	/// </summary>
	[Test]
	public async ValueTask HaltingByPidClaimsTheEntryAndStopsItRunning()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, "PidHalt");
		var ran = false;

		var gate = await ParkTheConsumer();
		long pid;

		try
		{
			await Scheduler.EnqueueWork(
				() =>
				{
					ran = true;
					return ValueTask.FromResult<CallState?>(null);
				},
				$"dbref:{thing}-halted", TaskScheduler.EnqueueGroup, thing);

			pid = await Scheduler.GetEnqueueTasks(thing).SingleAsync();

			await Assert.That(await Scheduler.HaltByPid(pid)).IsTrue();
			await Assert.That(await Scheduler.HaltByPid(pid)).IsFalse()
				.Because("the first call removed the entry, so there is nothing left to claim");
		}
		finally
		{
			gate.SetResult();
		}

		await Scheduler.DrainImmediateQueueForTests(DrainTimeout);

		await Assert.That(ran).IsFalse()
			.Because("the consumer drops an entry whose token was cancelled before it was read");
	}
}
