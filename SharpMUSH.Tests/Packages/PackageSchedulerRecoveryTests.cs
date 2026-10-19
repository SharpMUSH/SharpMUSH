using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Packages;

public sealed class PackageSchedulerRecoveryWorld : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>Actual scheduler cancellation joins native compensation before releasing package sequencing.</summary>
public class PackageSchedulerRecoveryTests
{
	[ClassDataSource<PackageSchedulerRecoveryWorld>(Shared = SharedType.PerClass)]
	public required PackageSchedulerRecoveryWorld World { get; init; }

	private ISharpDatabase Database => World.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => World.Services.GetRequiredService<IMediator>();
	private ILockService Locks => World.Services.GetRequiredService<ILockService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;

	[Test]
	public Task Halt_JoinsNativeCompensationBeforeTheNextPackageOperation() => CheckRecoveryAsync(false);

	[Test]
	public Task Shutdown_JoinsNativeCompensationBeforeDisposingTheOwnedQueue() => CheckRecoveryAsync(true);

	private async Task CheckRecoveryAsync(bool shutdown)
	{
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		using var requestCancellation = new CancellationTokenSource();
		// This queue belongs to this test. Its dependencies and native writes are real;
		// neither the host's singleton scheduler nor its lifetime is replaced or disposed.
		var queue = new Scheduler(World.CommandParser,
			World.Services.GetRequiredService<IConnectionService>(),
			World.Services.GetRequiredService<ISchedulerFactory>(),
			World.Services.GetRequiredService<IAttributeService>(), Mediator,
			NullLogger<Scheduler>.Instance);
		var runner = new PackageOperationRunner(queue, new PackageOperationGate(),
			Substitute.For<IWorldBackupService>(), World.Services.GetRequiredService<IPackageLifecycleRunner>(),
			NullLogger<PackageOperationRunner>.Instance);
		var forwardWritten = Signal();
		var releaseBody = Signal();
		var recoveryStarted = Signal();
		var releaseRecovery = Signal();
		var bodySettled = Signal();
		var laterStarted = false;
		Task? first = null;
		Task<PackageOperationOutcome<bool>>? later = null;
		Task? stopping = null;
		try
		{
			var unique = Guid.NewGuid().ToString("N");
			var package = $"scheduler-recovery-{unique}";
			var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
			AnySharpObject executor = pm;
			AnySharpContainer home = pm;
			var dbref = await Database.CreateThingAsync($"Scheduler Recovery {unique}", home, pm, home);
			var node = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();
			var objid = node.Object().DBRef.ToString();
			await Database.SetAttributeAsync(dbref, ["RECOVERY_VALUE"], MarkupText.Plain("original"), pm);
			await Assert.That(await Locks.SetAsync(executor, node, "Use", "#TRUE") is Success).IsTrue();
			var originalLock = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object().Locks["Use"];
			var baseline = new ManagedAttributeRecord(package, objid, "RECOVERY_VALUE", "original", "original-hash", "1.0.0");
			await Registry.UpsertManagedAttributeAsync(baseline);

			async Task AssertRestoredAsync()
			{
				var fresh = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object();
				await Assert.That(fresh.Locks["Use"]).IsEqualTo(originalLock);
				var attribute = await Database.GetAttributeAsync(dbref, ["RECOVERY_VALUE"]).LastOrDefaultAsync();
				await Assert.That(attribute?.Value.ToPlainText()).IsEqualTo("original");
				await Assert.That((await Registry.GetManagedAttributesAsync(package)).Single()).IsEqualTo(baseline);
			}

			first = runner.RunAsync("apply", async token =>
			{
				try
				{
					await using var writes = new PackageWriteTransaction(Mediator, Database, Database, Database, Registry,
						(IApplicationRegistryService)Database, Locks, pm);
					await Assert.That(await writes.SetLockAsync(node, "Use", "#FALSE", token) is Success).IsTrue();
					await writes.SetAttributeAsync(dbref, ["RECOVERY_VALUE"], MarkupText.Plain("changed"), pm, token);
					await writes.UpsertManagedAttributeAsync(baseline with { BaselineValue = "changed" });
					// This final inverse pauses first; all actual write inverses remain intact.
					writes.Track("test recovery handshake", async () =>
					{
						recoveryStarted.TrySetResult();
						await releaseRecovery.Task;
					});
					forwardWritten.TrySetResult();
					await releaseBody.Task.WaitAsync(token);
					token.ThrowIfCancellationRequested();
					return true;
				}
				finally
				{
					bodySettled.TrySetResult();
				}
			}, requestCancellation.Token);
			await forwardWritten.Task.WaitAsync(deadline.Token);
			var pid = queue.GetQueueEntries().Single().Pid;
			if (shutdown) stopping = queue.DisposeAsync().AsTask();
			else await Assert.That(await queue.HaltByPid(pid)).IsTrue();
			await recoveryStarted.Task.WaitAsync(deadline.Token);
			await Assert.That(bodySettled.Task.IsCompleted).IsFalse();
			if (stopping is not null) await Assert.That(stopping.IsCompleted).IsFalse();

			// HTTP observation ends while actual recovery still owns queue and sequence.
			requestCancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await first.WaitAsync(deadline.Token));
			deadline.Token.ThrowIfCancellationRequested();
			later = runner.RunAsync("apply", async _ =>
			{
				laterStarted = true;
				await AssertRestoredAsync();
				return true;
			});
			await Assert.That(later.IsCompleted).IsFalse();
			await Assert.That(laterStarted).IsFalse();
			await Assert.That(queue.GetQueueEntries().Count).IsEqualTo(1)
				.Because("the next operation must wait for real transaction recovery before admission");
			releaseRecovery.TrySetResult();
			await bodySettled.Task.WaitAsync(deadline.Token);
			if (stopping is not null) await stopping.WaitAsync(deadline.Token);
			var result = await later.WaitAsync(deadline.Token);
			if (shutdown)
			{
				result.Expect<PackageOperationRefused>();
				await Assert.That(laterStarted).IsFalse();
			}
			else
			{
				await Assert.That(result.Expect<PackageOperationRan<bool>>().Result).IsTrue();
				await queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(10));
			}
			await AssertRestoredAsync();
			await Assert.That(queue.GetQueueEntries()).IsEmpty();
		}
		finally
		{
			releaseBody.TrySetResult();
			releaseRecovery.TrySetResult();
			requestCancellation.Cancel();
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			if (stopping is null) stopping = queue.DisposeAsync().AsTask();
			await stopping.WaitAsync(cleanup.Token);
			if (first is not null) await ObserveSettledAsync(first, cleanup.Token);
			if (first is not null && forwardWritten.Task.IsCompleted)
				await bodySettled.Task.WaitAsync(cleanup.Token);
			if (later is not null) await ObserveSettledAsync(later, cleanup.Token);
		}
	}

	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static async Task ObserveSettledAsync(Task task, CancellationToken cancellationToken)
	{
		try
		{
			await task.WaitAsync(cancellationToken);
		}
		catch (Exception) when (task.IsCompleted)
		{
			// Assertions own the result; cleanup joins every started task.
		}
	}
}
