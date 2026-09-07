using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Services;

public class EngineReadinessTests
{
	[Test]
	public async Task ReadyWaitsForAllInputConsumers()
	{
		var bus = Substitute.For<IMessageBus>();
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new(typeof(TelnetInputMessage), "input", "input", (_, _, _) => Task.CompletedTask));
		using var handler = new StartupHandler(NullLogger<StartupHandler>.Instance,
			Substitute.For<IExpandedObjectDataService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			Substitute.For<IWikiService>(), bus, registry);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bus.Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>())
			.Returns(_ => { ready.TrySetResult(); return Task.CompletedTask; });
		await handler.StartedAsync(timeout.Token).WaitAsync(timeout.Token);
		await bus.DidNotReceive().Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>());
		registry.MarkActive("input");
		await ready.Task.WaitAsync(timeout.Token);
		await bus.Received(1).Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>());
		await handler.StoppingAsync(timeout.Token);
		await bus.Received(1).Publish(Arg.Any<MainProcessShutdownMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ShutdownCancelsReadinessWaitingForBroker()
	{
		var bus = Substitute.For<IMessageBus>();
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new(typeof(TelnetInputMessage), "input", "input", (_, _, _) => Task.CompletedTask));
		using var handler = new StartupHandler(NullLogger<StartupHandler>.Instance,
			Substitute.For<IExpandedObjectDataService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			Substitute.For<IWikiService>(), bus, registry);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await handler.StartedAsync(timeout.Token).WaitAsync(timeout.Token);
		await handler.StoppingAsync(timeout.Token).WaitAsync(timeout.Token);
		registry.MarkActive("input");
		await bus.DidNotReceive().Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>());
		await bus.Received(1).Publish(Arg.Any<MainProcessShutdownMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ReadinessPublishRetriesBrokerFailure()
	{
		var bus = Substitute.For<IMessageBus>();
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var attempts = 0;
		bus.Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				if (Interlocked.Increment(ref attempts) == 1)
					return Task.FromException(new TimeoutException("Broker unavailable"));
				ready.TrySetResult();
				return Task.CompletedTask;
			});
		using var handler = new StartupHandler(NullLogger<StartupHandler>.Instance,
			Substitute.For<IExpandedObjectDataService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			Substitute.For<IWikiService>(), bus);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
		await handler.StartedAsync(timeout.Token).WaitAsync(timeout.Token);
		await ready.Task.WaitAsync(timeout.Token);
		await handler.StoppingAsync(timeout.Token).WaitAsync(timeout.Token);
		await Assert.That(attempts).IsEqualTo(2);
	}

	[Test]
	public async Task DuplicateDurablesDoNotPreventReadinessOrHideMissingConsumers()
	{
		var registry = new NatsConsumerRegistry();
		registry.Registrations.Add(new(typeof(TelnetInputMessage), "input", "input", (_, _, _) => Task.CompletedTask));
		registry.Registrations.Add(registry.Registrations[0]);
		registry.Registrations.Add(new(typeof(TelnetInputMessage), "other", "other", (_, _, _) => Task.CompletedTask));
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var ready = registry.WaitUntilReadyAsync(timeout.Token);
		registry.MarkActive("unregistered");
		registry.MarkActive("input");
		await Assert.That(ready.IsCompleted).IsFalse();
		registry.MarkActive("other");
		await ready.WaitAsync(timeout.Token);
	}
}
