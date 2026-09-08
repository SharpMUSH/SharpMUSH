using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Tests.ConnectionServer.TestSchedulers;

namespace SharpMUSH.Tests.ConnectionServer;

public class ConnectionCleanupServiceTests
{
	[Test]
	public async Task OneFailedRecordDoesNotPreventOtherRecordsFromBeingCleaned()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var bus = Substitute.For<IMessageBus>();
		var records = Enumerable.Range(1, 20).Select(handle => ((long)handle, new ConnectionStateData
		{
			Handle = handle, State = "Connected", ConnectionType = "telnet", IpAddress = "ip", Hostname = "host",
			ConnectedAt = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow,
			Metadata = new() { ["SessionId"] = $"session-{handle}" }
		})).ToArray();
		store.GetAllConnectionsAsync(Arg.Any<CancellationToken>()).Returns(records);
		store.RemoveConnectionAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("failed record")));
		var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, Substitute.For<IConnectionServerService>(), bus,
			Substitute.For<IDescriptorGeneratorService>(), new TerminalReplayStore(), new ResumeTokenService(),
			new SessionSinkRegistry(), new DetachedSessionTracker(new ManualScheduler()), TimeSpan.FromMinutes(2));
		var cleanup = new ConnectionCleanupService(store, NullLogger<ConnectionCleanupService>.Instance,
			pump, new ConfigurationBuilder().Build(), bus);

		await cleanup.StartAsync(default).WaitAsync(TimeSpan.FromSeconds(3));

		await store.Received(20).RemoveConnectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
		await bus.Received(19).Publish(Arg.Any<ConnectionClosedMessage>(), Arg.Any<CancellationToken>());
	}
}
