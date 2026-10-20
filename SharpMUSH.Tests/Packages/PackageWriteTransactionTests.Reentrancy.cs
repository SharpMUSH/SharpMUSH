using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public partial class PackageWriteTransactionTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InverseCannotAwaitItsOwnTransactionClosure(bool explicitRevert)
	{
		var writes = await BeginAsync();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var attempted = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var callbackReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Task? nested = null;
		writes.Track("recursive inverse", async () =>
		{
			attempted++;
			entered.TrySetResult();
			await resume.Task;
			nested = explicitRevert ? writes.RevertAsync(new Error<string>("recursive failure")) : writes.DisposeAsync().AsTask();
			callbackReached.SetResult(nested.IsCompleted);
			await JoinOrReleaseAsync(nested, release.Task);
		});
		var close = writes.DisposeAsync().AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(close.IsCompleted).IsFalse();
			resume.TrySetResult();
			// The self-join must still reject after a real async suspension.
			await Assert.That(await callbackReached.Task.WaitAsync(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(async () => await close).Throws<PackageCompensationException>();
			if (nested is not { } inner) throw new InvalidOperationException("Inverse did not attempt closure");
			await Assert.That(async () => await inner).Throws<InvalidOperationException>();
			await Assert.That(async () => await writes.DisposeAsync()).Throws<PackageCompensationException>();
			await Assert.That(attempted).IsEqualTo(1);
		}
		finally
		{
			// Releases an erroneous self-wait deterministically, so a red test leaves no live undo.
			resume.TrySetResult();
			release.TrySetResult();
			try { await close; } catch (PackageCompensationException) { }
			if (nested is not null)
			{
				try { await nested; } catch (Exception ex) when (ex is InvalidOperationException or PackageCompensationException) { }
			}
		}
	}

	[Test]
	public async Task ForwardCallbackCannotAwaitItsOwnTransactionClosure()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var sourceReached = false;
		var callbackReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		PackageWriteTransaction? writes = null;
		Task? nested = null;
		var applications = new CallbackApplications((IApplicationRegistryService)Database, async () =>
		{
			if (writes is not { } transaction) throw new InvalidOperationException("Transaction not constructed");
			nested = transaction.DisposeAsync().AsTask();
			callbackReached.SetResult(nested.IsCompleted);
			await JoinOrReleaseAsync(nested, release.Task);
			sourceReached = true;
		});
		writes = new PackageWriteTransaction(Mediator, Database, Database, Database, Registry, applications,
			WebAppFactoryArg.Services.GetRequiredService<ILockService>(), await PackageManagerAsync());
		var forward = writes.RemoveApplicationAsync("recursive-forward-" + Guid.NewGuid().ToString("N"));
		try
		{
			await Assert.That(await callbackReached.Task.WaitAsync(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(async () => await forward).Throws<InvalidOperationException>();
			if (nested is not { } inner) throw new InvalidOperationException("Forward callback did not attempt closure");
			await Assert.That(async () => await inner).Throws<InvalidOperationException>();
			await Assert.That(sourceReached).IsFalse();
		}
		finally
		{
			release.TrySetResult();
			try { await forward; } catch (InvalidOperationException) { }
			await writes.DisposeAsync();
			if (nested is not null)
			{
				try { await nested; } catch (InvalidOperationException) { }
			}
		}
	}

	[Test]
	public async Task NestedTransactionInverseCannotAwaitAnActiveAncestor()
	{
		var first = await BeginAsync();
		var second = await BeginAsync();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task? ancestorClose = null;
		Task? childClose = null;
		second.Track("ancestor inverse", () =>
		{
			ancestorClose = first.DisposeAsync().AsTask();
			return JoinOrReleaseAsync(ancestorClose, release.Task);
		});
		first.Track("child inverse", () =>
		{
			childClose = second.DisposeAsync().AsTask();
			return JoinOrReleaseAsync(childClose, release.Task);
		});
		var close = first.DisposeAsync().AsTask();
		try
		{
			await Assert.That(close.IsCompleted).IsTrue();
			await Assert.That(async () => await close).Throws<PackageCompensationException>();
			if (ancestorClose is not { } ancestor) throw new InvalidOperationException("Ancestor closure not attempted");
			await Assert.That(async () => await ancestor).Throws<InvalidOperationException>();
		}
		finally
		{
			release.TrySetResult();
			await close.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (childClose is not null) await childClose.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (ancestorClose is not null) await ancestorClose.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	[Test]
	public async Task CompletedForwardContextDoesNotForbidLaterIdempotentDisposal()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		PackageWriteTransaction? writes = null;
		Task? captured = null;
		var applications = new CallbackApplications((IApplicationRegistryService)Database, () =>
		{
			if (writes is not { } transaction) throw new InvalidOperationException("Transaction not constructed");
			// Captures the active forward context, but closes only after that write and its owner
			// have finished. This tests expiration; it grants no ownership to detached source work.
			captured = Task.Run(async () => { await release.Task; await transaction.DisposeAsync(); });
			return Task.CompletedTask;
		});
		writes = new PackageWriteTransaction(Mediator, Database, Database, Database, Registry, applications,
			WebAppFactoryArg.Services.GetRequiredService<ILockService>(), await PackageManagerAsync());
		try
		{
			await writes.RemoveApplicationAsync("expired-forward-" + Guid.NewGuid().ToString("N"));
			writes.Commit();
			await writes.DisposeAsync();
			release.TrySetResult();
			if (captured is not { } callback) throw new InvalidOperationException("Context was not captured");
			await callback;
		}
		finally
		{
			release.TrySetResult();
			if (captured is not null) await captured;
			await writes.DisposeAsync();
		}
	}

	private static async Task JoinOrReleaseAsync(Task nested, Task release)
	{
		await Task.WhenAny(nested, release);
		if (!nested.IsCompleted) throw new InvalidOperationException("Test released an erroneous self-wait");
		await nested;
	}

	/// <summary>Real application storage with an extensible callback before its removal.</summary>
	private sealed class CallbackApplications(IApplicationRegistryService inner, Func<Task> beforeRemove) : IApplicationRegistryService
	{
		public Task UpsertApplicationAsync(RegisteredApplication application) => inner.UpsertApplicationAsync(application);
		public Task<Found<RegisteredApplication>> GetApplicationAsync(string slug) => inner.GetApplicationAsync(slug);
		public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync() => inner.GetApplicationsAsync();
		public async Task RemoveApplicationAsync(string slug)
		{
			await beforeRemove();
			await inner.RemoveApplicationAsync(slug);
		}
	}
}
