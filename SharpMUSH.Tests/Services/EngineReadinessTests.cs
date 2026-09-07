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
		var handler = new StartupHandler(NullLogger<StartupHandler>.Instance,
			Substitute.For<IExpandedObjectDataService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			Substitute.For<IWikiService>(), bus, registry);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var ready = handler.StartedAsync(timeout.Token);
		await bus.DidNotReceive().Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>());
		registry.MarkActive("input");
		await ready;
		await bus.Received(1).Publish(Arg.Any<MainProcessReadyMessage>(), Arg.Any<CancellationToken>());
		await handler.StoppingAsync(timeout.Token);
		await bus.Received(1).Publish(Arg.Any<MainProcessShutdownMessage>(), Arg.Any<CancellationToken>());
	}
}
