using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

public class NotifyRealityCancellationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RealityReadObservesExecutionCancellation(bool unboundHandle)
	{
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async ValueTask<bool> Read(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
			return true;
		}
		var reality = Substitute.For<IRealityPolicy>();
		reality.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(call => Read(call.Arg<CancellationToken>()));
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(call => Read(call.Arg<CancellationToken>()));
		var bus = Substitute.For<IMessageBus>();
		var listeners = Substitute.For<IListenerRoutingService>();
		var notify = new NotifyService(bus, Substitute.For<IConnectionService>(),
			Substitute.For<ILocalizationService>(), reality, listeners);
		var sender = new TestObjectFactory().CreatePlayer(1, "sender");
		var operation = (unboundHandle ? notify.Notify(1L, "output", sender)
			: notify.Notify(new DBRef(2), "output", sender)).AsTask();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEqualTo(budget.Token);
			cancel.Cancel();
			OperationCanceledException? cancellation = null;
			try { await operation.WaitAsync(TimeSpan.FromSeconds(2)); }
			catch (OperationCanceledException ex) { cancellation = ex; }
			await Assert.That(cancellation).IsNotNull();
			await Assert.That(cancellation!.CancellationToken).IsEqualTo(budget.Token);
			await Assert.That(listeners.ReceivedCalls().Any()).IsFalse();
			await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
		}
		finally
		{
			cleanup.Cancel();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}
}
