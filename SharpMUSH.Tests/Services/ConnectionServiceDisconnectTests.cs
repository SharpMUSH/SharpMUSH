using System.Collections.Concurrent;
using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ConnectionService.Disconnect"/>'s remaining-connections computation —
/// specifically the fix for the Codex review's Finding 2 on PR #902: two of the SAME player's
/// handles disconnecting at genuinely the same time must not both compute "1 remaining" by each
/// seeing only the other's (not-yet-removed) handle still present in session state.
/// </summary>
public class ConnectionServiceDisconnectTests
{
	private static ConcurrentDictionary<string, string> DefaultMetadata() =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "websocket"
		});

	private static async Task RegisterAsync(ConnectionService svc, long handle) =>
		await svc.Register(handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			DefaultMetadata());

	[Test]
	public async ValueTask Disconnect_SingleConnection_ReportsZeroRemaining()
	{
		var publisher = Substitute.For<IPublisher>();
		var captured = new List<ConnectionStateChangeNotification>();
		publisher.Publish(Arg.Any<ConnectionStateChangeNotification>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				captured.Add((ConnectionStateChangeNotification)ci[0]);
				return ValueTask.CompletedTask;
			});

		var svc = new ConnectionService(publisher);
		var playerRef = new DBRef(400, 0);
		await RegisterAsync(svc, 1);
		await svc.Bind(1, playerRef);

		await svc.Disconnect(1);

		var disconnectNotification = captured.Single(n => n.NewState == IConnectionService.ConnectionState.Disconnected);
		await Assert.That(disconnectNotification.RemainingConnections).IsEqualTo(0);
	}

	[Test]
	public async ValueTask Disconnect_OneOfTwoConnections_ReportsOneRemaining()
	{
		var publisher = Substitute.For<IPublisher>();
		var captured = new List<ConnectionStateChangeNotification>();
		publisher.Publish(Arg.Any<ConnectionStateChangeNotification>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				captured.Add((ConnectionStateChangeNotification)ci[0]);
				return ValueTask.CompletedTask;
			});

		var svc = new ConnectionService(publisher);
		var playerRef = new DBRef(401, 0);
		await RegisterAsync(svc, 1);
		await RegisterAsync(svc, 2);
		await svc.Bind(1, playerRef);
		await svc.Bind(2, playerRef);

		await svc.Disconnect(1);

		var disconnectNotification = captured.Single(n => n.NewState == IConnectionService.ConnectionState.Disconnected);
		await Assert.That(disconnectNotification.RemainingConnections).IsEqualTo(1);
	}

	/// <summary>
	/// Regression test for the Codex review's Finding 2 on PR #902: <c>Disconnect</c> publishes its
	/// state-change notification before removing the disconnecting handle from session state (needed
	/// so a handler can still look up that handle's own metadata), so a naive "count other handles
	/// still present for this player" computation done independently by two concurrent <c>Disconnect</c>
	/// calls for the SAME player's two handles would each see the other's handle as still connected,
	/// and both would report "1 remaining" — instead of the correct sequence, where whichever call
	/// actually finishes second sees 0.
	///
	/// <para>This forces the two <c>Disconnect</c> calls onto separate thread-pool threads, synchronized
	/// to start at the same instant via a <see cref="Barrier"/>, so <c>ConnectionService</c>'s internal
	/// lock around the remove-and-count step is genuinely raced rather than merely interleaved
	/// cooperatively on one thread (which would never reproduce the bug, since the first call would run
	/// to completion — including removing its own handle — before the second even starts). The fix's
	/// locking makes the outcome deterministic regardless of which thread wins the race: exactly one of
	/// the two notifications must report 0 remaining and the other 1, never {1, 1} (the pre-fix bug) and
	/// never a wrong total.</para>
	/// </summary>
	[Test]
	[Repeat(20)]
	public async ValueTask Disconnect_TwoHandlesForSamePlayerConcurrently_ExactlyOneSeesZeroRemaining()
	{
		var publisher = Substitute.For<IPublisher>();
		var captured = new ConcurrentBag<ConnectionStateChangeNotification>();
		publisher.Publish(Arg.Any<ConnectionStateChangeNotification>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				captured.Add((ConnectionStateChangeNotification)ci[0]);
				return ValueTask.CompletedTask;
			});

		var svc = new ConnectionService(publisher);
		var playerRef = new DBRef(402, 0);
		await RegisterAsync(svc, 1);
		await RegisterAsync(svc, 2);
		await svc.Bind(1, playerRef);
		await svc.Bind(2, playerRef);

		using var barrier = new Barrier(2);
		var t1 = Task.Run(async () =>
		{
			barrier.SignalAndWait();
			await svc.Disconnect(1);
		});
		var t2 = Task.Run(async () =>
		{
			barrier.SignalAndWait();
			await svc.Disconnect(2);
		});
		await Task.WhenAll(t1, t2);

		var remaining = captured
			.Where(n => n.NewState == IConnectionService.ConnectionState.Disconnected)
			.OrderBy(n => n.Handle)
			.Select(n => n.RemainingConnections)
			.ToList();

		await Assert.That(remaining.Count).IsEqualTo(2);
		await Assert.That(remaining).Contains(0);
		await Assert.That(remaining).Contains(1);
		await Assert.That(remaining.Count(v => v == 0)).IsEqualTo(1)
			.Because("exactly one of the two concurrent disconnects must see itself as the last " +
				"connection to leave — both reporting 1 remaining (the pre-fix race) would mean " +
				"neither ever triggers LASTLOGOUT, or the ordinary (non-final) disconnect wording, " +
				"even though the player is now fully disconnected");
	}
}
