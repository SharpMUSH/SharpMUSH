using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What happens when a brand-new connection is handed a handle number that reconciliation already
/// restored from a previous run.
///
/// <para>Connection state lives in a store that outlives the game server, and
/// <see cref="ConnectionService.ReconcileFromStateStoreAsync"/> restores it on startup — including
/// each handle's bound player and its LoggedIn state. The connection server, meanwhile, restarts its
/// descriptor numbering from the configured base, so after a restart it hands out the very same
/// handle numbers again. The two facts meet on the first connections of a restarted stack.</para>
///
/// <para>What this cost, before: <c>Register</c> refused to overwrite the reconciled entry, so the
/// new socket inherited a dead player's binding. <c>MUSHCodeParser.CommandParse</c> reads the
/// executor straight off <c>connectionService.Get(handle)?.Ref</c>, so a non-null Ref means "already
/// logged in" — the login token the portal sends was dispatched as an ordinary game command and
/// answered with "Huh?", and because the bound dbref belonged to a database that no longer existed,
/// every command after it died on "Cannot convert an None to a non-None value". The player's first
/// screen was a terminal that would not accept a single command until they reloaded the page.</para>
/// </summary>
public class ConnectionServiceReconciledHandleTests
{
	private const long Handle = 1000001;

	private static ConcurrentDictionary<string, string> Metadata() =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "websocket"
		});

	/// <summary>A service whose store reports one LoggedIn handle left over from a previous run.</summary>
	private static async Task<ConnectionService> ReconciledServiceAsync(string? stalePlayerObjid = "#12:1788329388321")
	{
		var store = Substitute.For<IConnectionStateStore>();
		store.GetAllConnectionsAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
			[
				(Handle, new ConnectionStateData
				{
					Handle = Handle,
					PlayerObjid = stalePlayerObjid,
					State = "LoggedIn",
					IpAddress = "127.0.0.1",
					Hostname = "localhost",
					ConnectionType = "websocket",
					ConnectedAt = DateTimeOffset.UtcNow.AddHours(-1),
					LastSeen = DateTimeOffset.UtcNow.AddHours(-1),
					Metadata = new Dictionary<string, string>()
				})
			]));

		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await service.ReconcileFromStateStoreAsync(
			_ => _ => ValueTask.CompletedTask,
			_ => _ => ValueTask.CompletedTask,
			() => Encoding.UTF8);
		return service;
	}

	[Test]
	public async ValueTask Register_OverAReconciledHandle_DoesNotInheritTheOldPlayerBinding()
	{
		var service = await ReconciledServiceAsync();
		await Assert.That(service.Get(Handle)?.Ref).IsNotNull()
			.Because("the reconciled entry is the precondition this test exists to break");

		await service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());

		await Assert.That(service.Get(Handle)?.Ref).IsNull()
			.Because("a new connection is nobody until it logs in; inheriting a binding makes the "
				+ "login token itself look like an in-game command");
	}

	[Test]
	public async ValueTask Register_OverAReconciledHandle_StartsConnectedNotLoggedIn()
	{
		var service = await ReconciledServiceAsync();

		await service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());

		await Assert.That(service.Get(Handle)?.State)
			.IsEqualTo(IConnectionService.ConnectionState.Connected);
	}

	/// <summary>
	/// The output function has to be the new socket's. Keeping the reconciled one points this
	/// connection's output at a transport that belongs to a process that is gone.
	/// </summary>
	[Test]
	public async ValueTask Register_OverAReconciledHandle_TakesTheNewOutputFunction()
	{
		var service = await ReconciledServiceAsync();
		var reachedNewSocket = false;

		await service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => { reachedNewSocket = true; return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());

		await service.Get(Handle)!.OutputFunction(Encoding.UTF8.GetBytes("hello"));

		await Assert.That(reachedNewSocket).IsTrue();
	}

	/// <summary>
	/// The guard being replaced was there for a real reason — a redelivered Register message for a
	/// live connection must not reset it. Only an entry this process never registered is stale.
	/// </summary>
	[Test]
	public async ValueTask Register_TwiceForALiveConnection_LeavesTheLoginAlone()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());
		await service.Bind(Handle, new DBRef(7, 1700000000));

		await service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, Metadata());

		await Assert.That(service.Get(Handle)?.Ref).IsNotNull()
			.Because("a duplicate Register for a connection this process owns must not log it out");
	}
}
