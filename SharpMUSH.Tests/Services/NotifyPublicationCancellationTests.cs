using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public class NotifyPublicationCancellationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublicationReceivesAndObservesCurrentExecutionToken(bool prompt)
	{
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var bus = Substitute.For<IMessageBus>();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task Publish(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
		}
		bus.HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>())
			.Returns(call => Publish(call.Arg<CancellationToken>()));
		bus.HandlePublish(Arg.Any<MarkupPromptMessage>(), Arg.Any<CancellationToken>())
			.Returns(call => Publish(call.Arg<CancellationToken>()));
		var notify = new NotifyService(bus, Substitute.For<IConnectionService>(), Substitute.For<ILocalizationService>(), Substitute.For<IRealityPolicy>());
		var operation = (prompt ? notify.Prompt(1L, "output", null) : notify.Notify(1L, "output", null)).AsTask();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEqualTo(budget.Token);
			cancel.Cancel();
			OperationCanceledException? cancellation = null;
			try { await operation.WaitAsync(TimeSpan.FromSeconds(2)); }
			catch (OperationCanceledException ex) { cancellation = ex; }
			await Assert.That(cancellation).IsNotNull();
			await Assert.That(cancellation!.CancellationToken).IsEqualTo(budget.Token);
		}
		finally
		{
			cleanup.Cancel();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}
}
