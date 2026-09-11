using Mediator;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

public class NotifyLocationCancellationTests
{
	[Test]
	[Arguments("player")]
	[Arguments("thing")]
	[Arguments("exit")]
	public async Task SenderLocationReadObservesExecutionCancellation(string kind)
	{
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		var factory = new TestObjectFactory();
		var room = factory.CreateRoom(100, "room");
		var sender = kind switch
		{
			"player" => factory.CreatePlayer(1, "actor"),
			"thing" => factory.CreateThing(1, "actor"),
			_ => factory.CreateExit(1, "actor", [], room)
		};
		var location = new AsyncRelation<AnySharpContainer>(async token =>
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
			return room;
		});
		switch (sender)
		{
			case SharpPlayer player: player.Location = location; break;
			case SharpThing thing: thing.Location = location; break;
			case SharpExit exit: exit.Location = location; break;
		}
		var bus = Substitute.For<IMessageBus>();
		var listeners = Substitute.For<IListenerRoutingService>();
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		var notify = new NotifyService(bus, Substitute.For<IConnectionService>(), Substitute.For<ILocalizationService>(), reality, listeners, Substitute.For<IMediator>());
		var operation = notify.Notify(new DBRef(2), "output", sender).AsTask();
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
