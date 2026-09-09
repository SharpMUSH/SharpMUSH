using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
/// </remarks>
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
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&ADESCRIBE {thing}=look me;look me"));

		return thing;
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
}
