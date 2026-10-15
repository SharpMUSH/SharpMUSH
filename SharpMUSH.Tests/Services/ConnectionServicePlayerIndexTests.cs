using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="ConnectionService.Get(DBRef)"/> reads a per-player index rather than scanning every
/// connection. Each way a handle gains, loses or changes its player has to move it in that index, or a
/// lookup by player misses a live connection or answers with a dead one.
/// </summary>
public class ConnectionServicePlayerIndexTests
{
	private static readonly DBRef Alice = new(500, 1);
	private static readonly DBRef Bob = new(501, 1);

	private static ConcurrentDictionary<string, string> Metadata() =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = "0",
			["LastConnectionSignal"] = "0",
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "websocket"
		});

	private static ValueTask RegisterAsync(ConnectionService service, long handle) =>
		service.Register(handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());

	private static async Task<long[]> HandlesOf(ConnectionService service, DBRef player) =>
		(await service.Get(player).Select(connection => connection.Handle).ToArrayAsync()).Order().ToArray();

	/// <summary>A publisher that keeps the remaining-connection count of each disconnect.</summary>
	private static IPublisher Recording(ConcurrentQueue<int?> remaining)
	{
		var publisher = Substitute.For<IPublisher>();
		publisher.Publish(Arg.Any<ConnectionStateChangeNotification>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (call[0] is ConnectionStateChangeNotification { NewState: IConnectionService.ConnectionState.Disconnected } notification)
				{
					remaining.Enqueue(notification.RemainingConnections);
				}
				return ValueTask.CompletedTask;
			});
		return publisher;
	}

	[Test]
	public async ValueTask Bind_ListsTheHandleUnderItsPlayerOnly()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await RegisterAsync(service, 1);
		await RegisterAsync(service, 2);
		await RegisterAsync(service, 3);

		await service.Bind(1, Alice);
		await service.Bind(2, Alice);
		await service.Bind(3, Bob);

		await Assert.That(await HandlesOf(service, Alice)).IsEquivalentTo([1L, 2L]);
		await Assert.That(await HandlesOf(service, Bob)).IsEquivalentTo([3L]);
		await Assert.That(await HandlesOf(service, new DBRef(500))).IsEmpty()
			.Because("lookups match the stored reference exactly, as the scan did");
	}

	[Test]
	public async ValueTask Rebind_MovesTheHandleToTheNewPlayer()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await RegisterAsync(service, 1);

		await service.Bind(1, Alice);
		await service.Bind(1, Bob);

		await Assert.That(await HandlesOf(service, Alice)).IsEmpty();
		await Assert.That(await HandlesOf(service, Bob)).IsEquivalentTo([1L]);
	}

	[Test]
	public async ValueTask Unbind_DropsTheHandleAndAnotherLoginFindsIt()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await RegisterAsync(service, 1);
		await service.Bind(1, Alice);

		await service.Unbind(1);
		await Assert.That(await HandlesOf(service, Alice)).IsEmpty();

		await service.Bind(1, Alice);
		await Assert.That(await HandlesOf(service, Alice)).IsEquivalentTo([1L]);
	}

	/// <summary>The disconnect count reads the index too, so a handle left behind by a logout would count.</summary>
	[Test]
	public async ValueTask Disconnect_AfterAnotherHandleLoggedOut_ReportsNoneRemaining()
	{
		var remaining = new ConcurrentQueue<int?>();
		var service = new ConnectionService(Recording(remaining));
		await RegisterAsync(service, 1);
		await RegisterAsync(service, 2);
		await service.Bind(1, Alice);
		await service.Bind(2, Alice);

		await service.Unbind(1);
		await service.Disconnect(2);

		await Assert.That(remaining).IsEquivalentTo(new int?[] { 0 });
	}

	[Test]
	public async ValueTask BindAccount_DropsTheCharacter()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await RegisterAsync(service, 1);
		await service.Bind(1, Alice);

		await service.BindAccount(1, "account-1");

		await Assert.That(await HandlesOf(service, Alice)).IsEmpty();
	}

	[Test]
	public async ValueTask Disconnect_DropsTheHandleAndKeepsTheOthers()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await RegisterAsync(service, 1);
		await RegisterAsync(service, 2);
		await service.Bind(1, Alice);
		await service.Bind(2, Alice);

		await service.Disconnect(1);
		await Assert.That(await HandlesOf(service, Alice)).IsEquivalentTo([2L]);

		await service.Disconnect(2);
		await Assert.That(await HandlesOf(service, Alice)).IsEmpty();
	}

	[Test]
	public async ValueTask Reconcile_IndexesRestoredLogins_AndANewSocketOnTheHandleDropsThem()
	{
		var store = Substitute.For<IConnectionStateStore>();
		store.GetAllConnectionsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
			[
				(7, new ConnectionStateData
				{
					Handle = 7,
					PlayerObjid = Alice.ToString(),
					State = "LoggedIn",
					IpAddress = "127.0.0.1",
					Hostname = "localhost",
					ConnectionType = "websocket",
					ConnectedAt = DateTimeOffset.UnixEpoch,
					LastSeen = DateTimeOffset.UnixEpoch,
					Metadata = new Dictionary<string, string>()
				})
			]));
		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await service.ReconcileFromStateStoreAsync(
			_ => _ => ValueTask.CompletedTask, _ => _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await Assert.That(await HandlesOf(service, Alice)).IsEquivalentTo([7L]);

		await RegisterAsync(service, 7);

		await Assert.That(await HandlesOf(service, Alice)).IsEmpty()
			.Because("the new socket on a restored handle is nobody until it logs in");
	}

	/// <summary>
	/// Logins, logouts and disconnects for the same players from many threads at once: afterwards the
	/// index lists exactly the connections a full scan finds, and disconnecting them all counts down to none.
	/// </summary>
	[Test]
	[Repeat(10)]
	public async ValueTask ConcurrentChanges_LeaveTheIndexMatchingAFullScan()
	{
		var remaining = new ConcurrentQueue<int?>();
		var service = new ConnectionService(Recording(remaining));
		var players = new[] { Alice, Bob };
		for (var handle = 1L; handle <= 64; handle++) await RegisterAsync(service, handle);

		await Parallel.ForAsync(1L, 65L, async (handle, _) =>
		{
			for (var round = 0; round < 20; round++)
			{
				var step = (handle + round) % 4;
				await (step switch
				{
					0 => service.Bind(handle, players[round % 2]),
					1 => service.Unbind(handle),
					2 => service.Bind(handle, players[(round + 1) % 2]),
					_ => service.BindAccount(handle, "account")
				});
			}

			if (handle % 3 == 0) await service.Disconnect(handle);
		});

		foreach (var player in players)
		{
			var scanned = (await service.GetAll().Where(c => c.Ref == player).Select(c => c.Handle).ToArrayAsync()).Order().ToArray();
			await Assert.That(await HandlesOf(service, player)).IsEquivalentTo(scanned);

			remaining.Clear();
			foreach (var handle in scanned) await service.Disconnect(handle);
			await Assert.That(remaining).IsEquivalentTo(Enumerable.Range(0, scanned.Length).Reverse().Select(count => (int?)count));
		}
	}
}
