using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Native store/mediator/lock compensation under an externally cancelled execution budget.</summary>
public class PackageCancellationRecoveryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ILockService Locks => WebAppFactoryArg.Services.GetRequiredService<ILockService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;

	private async Task<PackageWriteTransaction> BeginAsync(TimeProvider? clock = null) => new(
		Mediator, Database, Database, Database, Registry, (IApplicationRegistryService)Database,
		Locks, (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>())
	{
		RecoveryClock = clock ?? TimeProvider.System
	};

	[Test]
	public async Task Dispose_FailedInverseIsObservableAndOtherInversesAreAttempted()
	{
		var writes = await BeginAsync();
		var attempted = new List<string>();
		writes.Track("oldest", () => { attempted.Add("oldest"); return Task.CompletedTask; });
		writes.Track("broken", () =>
		{
			attempted.Add("broken");
			throw new IOException("inverse could not write");
		});
		writes.Track("newest", () => { attempted.Add("newest"); return Task.CompletedTask; });

		async Task UnwindAsync()
		{
			await using var pending = writes;
			throw new InvalidOperationException("Original operation exception.");
		}

		var failure = await CompensationFailureAsync(UnwindAsync());
		await Assert.That(string.Join(",", attempted)).IsEqualTo("newest,broken,oldest");
		await Assert.That(string.Join("; ", failure.UnrestoredSteps)).IsEqualTo("broken (inverse could not write)");
		// The failed attempt has settled and must not replay on subsequent disposal.
		await writes.DisposeAsync();
		await Assert.That(attempted.Count).IsEqualTo(3);
	}

	[Test]
	public async Task Revert_CombinesOriginalErrorWithFailedInverse()
	{
		await using var writes = await BeginAsync();
		writes.Track("broken", () => throw new IOException("inverse could not write"));
		var failure = await writes.RevertAsync(new Error<string>("Original operation failure."));
		await Assert.That(failure.Value).IsEqualTo(
			"Original operation failure. Its changes were undone except: broken (inverse could not write).");
	}

	[Test]
	public async Task RecoveryDeadline_JoinsStartedInverseAndReportsUnstartedSteps()
	{
		using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var clock = new ManualTimerProvider();
		var writes = await BeginAsync(clock);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var olderAttempted = false;
		var recoveryToken = CancellationToken.None;
		writes.Track("older", () => { olderAttempted = true; return Task.CompletedTask; });
		writes.Track("started", async () =>
		{
			recoveryToken = ExecutionBudget.CurrentToken;
			entered.TrySetResult();
			// Deliberately noncooperative: the owner must still join a started native inverse.
			await release.Task;
		});
		var disposal = writes.DisposeAsync().AsTask();
		try
		{
			await entered.Task.WaitAsync(guard.Token);
			clock.FireDeadline();
			await Assert.That(recoveryToken.IsCancellationRequested).IsTrue();
			await Assert.That(disposal.IsCompleted).IsFalse();
			await Assert.That(olderAttempted).IsFalse();
		}
		finally
		{
			release.TrySetResult();
			await disposal.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}

		var failure = await CompensationFailureAsync(disposal);
		await Assert.That(olderAttempted).IsFalse();
		await Assert.That(string.Join("; ", failure.UnrestoredSteps))
			.IsEqualTo("older (not attempted: recovery deadline exceeded)");
	}

	private static async Task<PackageCompensationException> CompensationFailureAsync(Task operation)
	{
		try
		{
			await operation;
		}
		catch (PackageCompensationException exception)
		{
			return exception;
		}

		throw new InvalidOperationException("Expected an observable compensation failure.");
	}

	private sealed class ManualTimerProvider : TimeProvider
	{
		private Action? _fire;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_fire = () => callback(state);
			return new ManualTimer(() => _fire = null);
		}

		public void FireDeadline() => (_fire ?? throw new InvalidOperationException("No recovery timer."))();

		private sealed class ManualTimer(Action dispose) : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() => dispose();
			public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
		}
	}

	[Test]
	public Task Dispose_RestoresPersistedStateDespiteCancelledExecutionBudget() => CheckRecoveryAsync(false);

	[Test]
	public Task Revert_RestoresPersistedStateDespiteCancelledExecutionBudget() => CheckRecoveryAsync(true);

	private async Task CheckRecoveryAsync(bool explicitRevert)
	{
		var unique = Guid.NewGuid().ToString("N");
		var package = $"cancel-recovery-{unique}";
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		AnySharpObject executor = pm;
		AnySharpContainer home = pm;
		var dbref = await Database.CreateThingAsync($"Recovery {unique}", home, pm, home);
		var node = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();
		var objid = node.Object().DBRef.ToString();
		await Database.SetAttributeAsync(dbref, ["RECOVERY_VALUE"], MarkupText.Plain("original"), pm);
		var seededLock = await Locks.SetAsync(executor, node, "Use", "#TRUE");
		await Assert.That(seededLock is Success).IsTrue();
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
			package, objid, "RECOVERY_VALUE", "original", "original-hash", "1.0.0"));
		var before = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object().Locks["Use"];
		var baselineBefore = (await Registry.GetManagedAttributesAsync(package)).Single();

		using (var executionCancellation = new CancellationTokenSource())
		using (var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, executionCancellation.Token))
		using (budget.Enter())
		{
			var writes = new PackageWriteTransaction(Mediator, Database, Database, Database, Registry,
				(IApplicationRegistryService)Database, Locks, pm);
			try
			{
				var changed = await writes.SetLockAsync(node, "Use", "#FALSE", executionCancellation.Token);
				await Assert.That(changed is Success).IsTrue();
				await writes.SetAttributeAsync(dbref, ["RECOVERY_VALUE"], MarkupText.Plain("changed"), pm,
					executionCancellation.Token);
				await writes.UpsertManagedAttributeAsync(baselineBefore with { BaselineValue = "changed" });
				// All forward mutations have settled before cancellation; there is no timing race.
				var forward = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object();
				await Assert.That(forward.Locks["Use"].LockString).IsEqualTo("#FALSE");
				executionCancellation.Cancel();
				await Assert.That(ExecutionBudget.CurrentToken.IsCancellationRequested).IsTrue();
				if (explicitRevert)
				{
					// The decisive assertion is persisted state below, not the error's wording.
					await writes.RevertAsync(new Error<string>("Cancelled before commit."));
				}
			}
			finally
			{
				// Actual compensation; no replacement inverse or cancellation policy in the test.
				// For explicit Revert this is an already-settled no-op.
				await writes.DisposeAsync();
			}
		}

		// Fresh native reads after leaving the cancelled ambient scope.
		var restored = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object();
		await Assert.That(restored.Locks["Use"]).IsEqualTo(before);
		var attribute = await Database.GetAttributeAsync(dbref, ["RECOVERY_VALUE"]).LastOrDefaultAsync();
		await Assert.That(attribute?.Value.ToPlainText()).IsEqualTo("original");
		await Assert.That((await Registry.GetManagedAttributesAsync(package)).Single()).IsEqualTo(baselineBefore);
	}
}
