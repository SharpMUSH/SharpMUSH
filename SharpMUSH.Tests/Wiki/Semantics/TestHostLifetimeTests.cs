using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class TestHostLifetimeTests
{
	[Test]
	public async Task ConfigurationBindsOnlyOneRootAndInnerConstructionOccursOnce()
	{
		await using var services = new ServiceCollection().BuildServiceProvider();
		await using var foreign = new ServiceCollection().BuildServiceProvider();
		var inner = new ControlledHost(services);
		inner.ReleaseDisposal.TrySetResult();
		var constructions = 0;
		await using var owner = new TestHostLifetime(_ => { constructions++; return inner; });
		await Assert.That(() => { _ = owner.Services; }).Throws<InvalidOperationException>();
		owner.Bind(services);
		owner.Bind(services);
		await Assert.That(() => owner.Bind(foreign)).Throws<InvalidOperationException>();
		await Assert.That(ReferenceEquals(owner.Services, services)).IsTrue();
		await owner.StartAsync();
		await owner.StartAsync();
		await Assert.That(constructions).IsEqualTo(1);
		await Assert.That(inner.Starts).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentDisposalAndChildCallerJoinActualInnerCompletion()
	{
		await using var services = new ServiceCollection().BuildServiceProvider();
		var inner = new ControlledHost(services);
		var owner = new TestHostLifetime(_ => inner);
		owner.Bind(services);
		_ = owner.Services;
		var childEntered = Signal();
		Task? child = null;
		inner.OnDisposal = () => child = Task.Run(async () =>
		{
			// Launched inside the actual disposal flow: inherited execution context cannot turn this
			// external caller into a falsely completed self-disposal invocation.
			var joined = owner.DisposeAsync().AsTask();
			childEntered.TrySetResult();
			await joined;
		});
		var first = owner.DisposeAsync().AsTask();
		var second = owner.DisposeAsync().AsTask();
		var stopping = owner.StopAsync();
		try
		{
			await inner.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await childEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(first.IsCompleted || second.IsCompleted || stopping.IsCompleted || child!.IsCompleted).IsFalse();
			await Assert.That(inner.Disposals).IsEqualTo(1);
		}
		finally
		{
			inner.ReleaseDisposal.TrySetResult();
			await Task.WhenAll(first, second, stopping);
			if (child is not null) await child;
			await owner.DisposeAsync();
		}
		await Assert.That(inner.Closed).IsTrue();
		await Assert.That(inner.Stops).IsEqualTo(1);
		await Assert.That(inner.Disposals).IsEqualTo(1);
	}

	[Test]
	public async Task DisposalDrainsAnAlreadyRunningStopBeforeClosingInnerHost()
	{
		await using var services = new ServiceCollection().BuildServiceProvider();
		var inner = new ControlledHost(services) { HoldStop = true };
		var owner = new TestHostLifetime(_ => inner);
		owner.Bind(services);
		_ = owner.Services;
		var stopping = owner.StopAsync();
		Task? disposing = null;
		try
		{
			await inner.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			disposing = owner.DisposeAsync().AsTask();
			await Assert.That(inner.Disposals).IsEqualTo(0);
			await Assert.That(disposing.IsCompleted).IsFalse();
			inner.ReleaseStop.TrySetResult();
			await inner.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(disposing.IsCompleted).IsFalse();
		}
		finally
		{
			inner.ReleaseStop.TrySetResult();
			inner.ReleaseDisposal.TrySetResult();
			await stopping;
			if (disposing is not null) await disposing;
			await owner.DisposeAsync();
		}
		await Assert.That(inner.Stops).IsEqualTo(1);
		await Assert.That(inner.Disposals).IsEqualTo(1);
	}

	[Test]
	public async Task ShutdownAndDisposalFailuresRemainObservableToEveryCaller()
	{
		await using var services = new ServiceCollection().BuildServiceProvider();
		var stopFailure = new InvalidOperationException("controlled stop failure");
		var disposeFailure = new ApplicationException("controlled disposal failure");
		var inner = new ControlledHost(services) { StopFailure = stopFailure, DisposeFailure = disposeFailure };
		inner.ReleaseDisposal.TrySetResult();
		var owner = new TestHostLifetime(_ => inner);
		owner.Bind(services);
		_ = owner.Services;
		var first = await Observe(owner.DisposeAsync().AsTask());
		var second = await Observe(owner.DisposeAsync().AsTask());
		var lateStop = await Observe(owner.StopAsync());
		if (first is not AggregateException aggregate) throw new InvalidOperationException("Both cleanup failures must be retained.", first);
		await Assert.That(aggregate.InnerExceptions.Any(error => ReferenceEquals(error, stopFailure))).IsTrue();
		await Assert.That(aggregate.InnerExceptions.Any(error => ReferenceEquals(error, disposeFailure))).IsTrue();
		await Assert.That(ReferenceEquals(first, second) && ReferenceEquals(first, lateStop)).IsTrue();
		await Assert.That(inner.Closed).IsTrue();
		await Assert.That(inner.Stops).IsEqualTo(1);
		await Assert.That(inner.Disposals).IsEqualTo(1);
		await Assert.That(() => owner.StartAsync()).Throws<ObjectDisposedException>();
	}

	[Test]
	public async Task UnboundDisposalCreatesNoHostAndRevokesLaterBinding()
	{
		await using var services = new ServiceCollection().BuildServiceProvider();
		var constructions = 0;
		var owner = new TestHostLifetime(_ => { constructions++; return new ControlledHost(services); });
		await owner.DisposeAsync();
		await owner.DisposeAsync();
		await Assert.That(() => owner.Bind(services)).Throws<ObjectDisposedException>();
		await Assert.That(() => { _ = owner.Services; }).Throws<ObjectDisposedException>();
		await Assert.That(constructions).IsEqualTo(0);
	}

	[Test]
	public async Task FailedInnerConstructionStillDrainsBoundRoot()
	{
		var resource = new AsyncRootResource();
		var services = new ServiceCollection().AddSingleton(_ => resource).BuildServiceProvider();
		var constructions = 0;
		var owner = new TestHostLifetime(sp =>
		{
			constructions++;
			_ = sp.GetRequiredService<AsyncRootResource>();
			throw new InvalidOperationException("Controlled inner host construction failure.");
		});
		owner.Bind(services);
		Task? closing = null;
		try
		{
			await Assert.That(() => { _ = owner.Services; }).Throws<InvalidOperationException>();
			closing = owner.DisposeAsync().AsTask();
			await Task.WhenAny(resource.Entered.Task, closing).WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(resource.Entered.Task.IsCompleted).IsTrue();
			await Assert.That(closing.IsCompleted).IsFalse();
			resource.Release.TrySetResult();
			await closing;
			await Assert.That(resource.Closed).IsTrue();
			await Assert.That(resource.Disposals).IsEqualTo(1);
			await Assert.That(constructions).IsEqualTo(1);
		}
		finally
		{
			resource.Release.TrySetResult();
			try { if (closing is not null) await closing; }
			finally { await services.DisposeAsync(); }
		}
	}

	private sealed class AsyncRootResource : IAsyncDisposable
	{
		public TaskCompletionSource Entered { get; } = Signal();
		public TaskCompletionSource Release { get; } = Signal();
		public bool Closed { get; private set; }
		public int Disposals;

		public async ValueTask DisposeAsync()
		{
			Interlocked.Increment(ref Disposals);
			Entered.TrySetResult();
			await Release.Task;
			Closed = true;
		}
	}

	private static async Task<Exception?> Observe(Task task)
	{
		try { await task; return null; }
		catch (Exception error) { return error; }
	}

	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private sealed class ControlledHost(IServiceProvider services) : IHost, IAsyncDisposable
	{
		public IServiceProvider Services => services;
		public TaskCompletionSource StopEntered { get; } = Signal();
		public TaskCompletionSource ReleaseStop { get; } = Signal();
		public TaskCompletionSource DisposalEntered { get; } = Signal();
		public TaskCompletionSource ReleaseDisposal { get; } = Signal();
		public Action? OnDisposal { get; set; }
		public Exception? StopFailure { get; init; }
		public Exception? DisposeFailure { get; init; }
		public bool HoldStop { get; init; }
		public bool Closed { get; private set; }
		public int Starts;
		public int Stops;
		public int Disposals;

		public Task StartAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref Starts);
			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref Stops);
			StopEntered.TrySetResult();
			if (HoldStop) await ReleaseStop.Task;
			if (StopFailure is not null) throw StopFailure;
		}

		public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
		public async ValueTask DisposeAsync()
		{
			Interlocked.Increment(ref Disposals);
			OnDisposal?.Invoke();
			DisposalEntered.TrySetResult();
			await ReleaseDisposal.Task;
			Closed = true;
			if (DisposeFailure is not null) throw DisposeFailure;
		}
	}
}
