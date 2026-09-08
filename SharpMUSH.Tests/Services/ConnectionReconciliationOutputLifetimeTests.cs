using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The output delegates reconciliation builds for connections that survived an engine restart
/// outlive startup by the whole life of those connections, so they must not be tied to the token
/// the host passes to <see cref="IHostedService.StartAsync"/>.
///
/// <para>That token's source is disposed the moment <c>Host.StartAsync</c> returns, and a host
/// configured with <c>HostOptions.StartupTimeout</c> cancels it outright. Either way it stops
/// meaning anything once the game is up — but a delegate that captured it would keep handing it to
/// the message bus for every line the game ever sends a player who was connected before the
/// restart. Those are exactly the players the socket-preservation work exists to keep online.</para>
/// </summary>
public class ConnectionReconciliationOutputLifetimeTests
{
	private const long Handle = 4242;

	private static IConnectionStateStore StoreWithOneSurvivor()
	{
		var store = Substitute.For<IConnectionStateStore>();
		store.GetAllConnectionsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
			[
				(Handle, new ConnectionStateData
				{
					Handle = Handle,
					PlayerObjid = null,
					State = "Connected",
					IpAddress = "127.0.0.1",
					Hostname = "localhost",
					ConnectionType = "telnet",
					ConnectedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
					LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5),
					Metadata = new Dictionary<string, string>()
				})
			]));
		return store;
	}

	[Test]
	public async ValueTask RestoredConnectionsKeepReceivingOutputAfterStartupIsOver()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(Substitute.For<IPublisher>(), StoreWithOneSurvivor());
		var reconciliation = new ConnectionReconciliationService(connections, bus,
			NullLogger<ConnectionReconciliationService>.Instance);

		using var startup = new CancellationTokenSource();
		await reconciliation.StartAsync(startup.Token);
		// The host is up; whatever it used to bound startup is finished with.
		await startup.CancelAsync();

		var restored = connections.Get(Handle);
		await Assert.That(restored).IsNotNull()
			.Because("the restored entry is the precondition this test exists to break");

		await restored!.OutputFunction("You awaken where you left off.\r\n"u8.ToArray());
		await restored.PromptOutputFunction("> "u8.ToArray());

		await bus.Received(1).Publish(Arg.Any<TelnetOutputMessage>(),
			Arg.Is<CancellationToken>(token => !token.IsCancellationRequested));
		await bus.Received(1).Publish(Arg.Any<TelnetPromptMessage>(),
			Arg.Is<CancellationToken>(token => !token.IsCancellationRequested));
	}
}
