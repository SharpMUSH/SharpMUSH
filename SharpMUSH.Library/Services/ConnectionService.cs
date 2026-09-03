using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Library.Services;

public class ConnectionService(
	IPublisher publisher,
	IConnectionStateStore? stateStore = null,
	ITelemetryService? telemetryService = null) : IConnectionService
{
	/// <summary>
	/// Metadata key marking an entry this process did not register itself, but restored from the
	/// state store on startup. It is the one thing that distinguishes a stale handle from a live one,
	/// and it is dropped the moment a real connection claims the handle.
	/// </summary>
	private const string ReconciledMarker = "ReconciledFromStateStore";

	private readonly ConcurrentDictionary<long, IConnectionService.ConnectionData> _sessionState = [];
	private readonly List<Action<(long handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState)>> _handlers = [];

	public async ValueTask Disconnect(long handle)
	{
		var get = Get(handle);
		if (get is null) return;

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(get.Handle, get.Ref, get.State, IConnectionService.ConnectionState.Disconnected));
		}

		await publisher.Publish(new ConnectionStateChangeNotification(get.Handle, get.Ref, get.State,
			IConnectionService.ConnectionState.Disconnected));

		_sessionState.Remove(handle, out _);

		if (stateStore != null)
		{
			await stateStore.RemoveConnectionAsync(handle);
		}

		telemetryService?.RecordConnectionEvent("disconnected");
		UpdateConnectionMetrics();
	}

	public IConnectionService.ConnectionData? Get(long handle) =>
		_sessionState.GetValueOrDefault(handle);

	public IAsyncEnumerable<IConnectionService.ConnectionData> Get(DBRef reference) =>
		_sessionState.Values
			.ToAsyncEnumerable()
			.Where(x => x.Ref.HasValue)
			.Where(x => x.Ref!.Value.Equals(reference));

	public IAsyncEnumerable<IConnectionService.ConnectionData> GetAll() =>
		_sessionState.Values
			.ToAsyncEnumerable();

	public void ListenState(Action<(long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler) =>
		_handlers.Add(handler);

	public async ValueTask Bind(long handle, DBRef player, bool firstLogin = false)
	{
		var get = Get(handle);
		if (get is null) return;

		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during Login."),
			(_, y) => y with { Ref = player, State = IConnectionService.ConnectionState.LoggedIn });

		if (stateStore != null)
		{
			await stateStore.SetPlayerBindingAsync(handle, player.ToString());
		}

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, player, get.State, IConnectionService.ConnectionState.LoggedIn));
		}

		telemetryService?.RecordConnectionEvent("logged_in");
		UpdateConnectionMetrics();

		await publisher.Publish(new ConnectionStateChangeNotification(handle, player, get.State,
			IConnectionService.ConnectionState.LoggedIn, firstLogin));
	}

	public async ValueTask Unbind(long handle)
	{
		var get = Get(handle);
		if (get is null || get.Ref is null) return;

		var formerRef = get.Ref;

		// State is updated before the notification is published, so a PLAYER`DISCONNECT handler asking
		// for the player's remaining connections does not count the one that is leaving.
		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during Logout."),
			(_, y) => y with { Ref = null, State = IConnectionService.ConnectionState.Connected });

		if (stateStore != null)
		{
			// A null objid is how the store spells "bound to nobody"; the reconciled State has to move
			// with it, or a restart would restore a handle that claims to be logged in with no player.
			await stateStore.SetPlayerBindingAsync(handle, null);
			await stateStore.UpdateMetadataAsync(handle, "State", nameof(IConnectionService.ConnectionState.Connected));
		}

		foreach (var handler in _handlers)
		{
			handler((handle, formerRef, get.State, IConnectionService.ConnectionState.Connected));
		}

		telemetryService?.RecordConnectionEvent("logged_out");
		UpdateConnectionMetrics();

		await publisher.Publish(new ConnectionStateChangeNotification(handle, formerRef, get.State,
			IConnectionService.ConnectionState.Connected));
	}

	public async ValueTask BindAccount(long handle, string accountId)
	{
		var get = Get(handle);
		if (get is null) return;

		var oldState = get.State;
		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during BindAccount."),
			(_, y) =>
			{
				y.Metadata["AccountId"] = accountId;
				return y with { State = IConnectionService.ConnectionState.AccountMode };
			});

		if (stateStore != null)
		{
			await stateStore.UpdateMetadataAsync(handle, "AccountId", accountId);
			await stateStore.UpdateMetadataAsync(handle, "State", "AccountMode");
		}

		foreach (var handler in _handlers)
		{
			handler((handle, null, oldState, IConnectionService.ConnectionState.AccountMode));
		}

		await publisher.Publish(new ConnectionStateChangeNotification(handle, null, oldState,
			IConnectionService.ConnectionState.AccountMode));

		telemetryService?.RecordConnectionEvent("account_mode");
		UpdateConnectionMetrics();
	}

	public void Update(long handle, string key, string value)
	{
		var get = Get(handle);
		if (get is null) return;

		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during update."),
			(_, y) =>
			{
				y.Metadata.AddOrUpdate(key, value, (_, _) => value);
				return y;
			});

		// Update Redis if available (fire and forget for performance)
		if (stateStore != null)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await stateStore.UpdateMetadataAsync(handle, key, value);
				}
				catch
				{
					// Ignore errors in background update
				}
			});
		}
	}

	public void IncrementMetadata(long handle, string key)
	{
		if (Get(handle) is null) return;

		string? newValue = null;
		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during update."),
			(_, y) =>
			{
				y.Metadata.AddOrUpdate(key, "1",
					(_, existing) =>
					{
						var next = (int.TryParse(existing, out var current) ? current : 0) + 1;
						return next.ToString();
					});
				newValue = y.Metadata[key];
				return y;
			});

		// Update Redis if available (fire and forget for performance)
		if (stateStore != null && newValue != null)
		{
			var captured = newValue;
			_ = Task.Run(async () =>
			{
				try
				{
					await stateStore.UpdateMetadataAsync(handle, key, captured);
				}
				catch
				{
					// Ignore errors in background update
				}
			});
		}
	}

	public async ValueTask Register(long handle, string ipaddr, string host,
		string connectionType,
		Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction, Func<Encoding> encoding,
		ConcurrentDictionary<string, string>? metaData = null)
	{
		var metadata = metaData ?? new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			{"ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
			{"LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
			{"InternetProtocolAddress", ipaddr},
			{"HostName", host},
			{"ConnectionType", connectionType}
		});

		var newEntry = new IConnectionService.ConnectionData(handle, null, IConnectionService.ConnectionState.Connected,
			outputFunction, promptOutputFunction, encoding, metadata);

		// A handle that is already known has two very different explanations, and they need opposite
		// answers:
		//
		//   * a redelivered Register for a connection THIS process already owns (the state store is
		//     at-least-once). Overwriting would silently log a live player out, so it is ignored.
		//
		//   * an entry restored by ReconcileFromStateStoreAsync. Connection state outlives the game
		//     server, while the connection server restarts its descriptor numbering from the same
		//     configured base — so after a restart a brand-new socket is handed a handle number the
		//     store still describes, complete with the previous occupant's player binding and its
		//     LoggedIn state. Ignoring THAT is what made the new connection inherit a dead player:
		//     CommandParse takes the executor from Get(handle)?.Ref, so the connection was treated as
		//     already logged in and the login token it sent next was dispatched as an ordinary game
		//     command ("Huh?"). Its output function was the reconciled one too, pointing at a
		//     transport that no longer exists. A real socket beats a remembered one.
		var claimed = true;
		_sessionState.AddOrUpdate(handle, newEntry, (_, existing) =>
		{
			if (existing.Metadata.ContainsKey(ReconciledMarker)) return newEntry;
			claimed = false;
			return existing;
		});

		if (!claimed) return;

		if (stateStore != null)
		{
			await stateStore.SetConnectionAsync(handle, new ConnectionStateData
			{
				Handle = handle,
				PlayerObjid = null,
				State = "Connected",
				IpAddress = ipaddr,
				Hostname = host,
				ConnectionType = connectionType,
				ConnectedAt = DateTimeOffset.UtcNow,
				LastSeen = DateTimeOffset.UtcNow,
				Metadata = new Dictionary<string, string>(metadata)
			});
		}

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
		}

		await publisher.Publish(new ConnectionStateChangeNotification(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));

		telemetryService?.RecordConnectionEvent("connected");
		UpdateConnectionMetrics();
	}

	/// <summary>
	/// Reconcile state from Redis on startup.
	/// Should be called during application initialization.
	/// </summary>
	public async Task ReconcileFromStateStoreAsync(
		Func<long, Func<byte[], ValueTask>> createOutputFunction,
		Func<long, Func<byte[], ValueTask>> createPromptOutputFunction,
		Func<Encoding> encodingFunction)
	{
		if (stateStore == null) return;

		var connections = await stateStore.GetAllConnectionsAsync();

		foreach (var (handle, data) in connections)
		{
			// Skip if already in memory (shouldn't happen on startup)
			if (_sessionState.ContainsKey(handle)) continue;

			var state = data.State switch
			{
				"LoggedIn" => IConnectionService.ConnectionState.LoggedIn,
				"AccountMode" => IConnectionService.ConnectionState.AccountMode,
				"Connected" => IConnectionService.ConnectionState.Connected,
				_ => IConnectionService.ConnectionState.Connected
			};

			var metadata = new ConcurrentDictionary<string, string>(data.Metadata);
			// Marked so Register can tell this remembered entry from a live one it owns; see Register.
			metadata[ReconciledMarker] = "1";

			_sessionState.TryAdd(handle, new IConnectionService.ConnectionData(
				handle,
				data.PlayerObjid is null ? null : DBRef.Parse(data.PlayerObjid),
				state,
				createOutputFunction(handle),
				createPromptOutputFunction(handle),
				encodingFunction,
				metadata
			));
		}

		UpdateConnectionMetrics();
	}

	private void UpdateConnectionMetrics()
	{
		var activeConnections = _sessionState.Count(x => x.Value.State is IConnectionService.ConnectionState.Connected or IConnectionService.ConnectionState.AccountMode or IConnectionService.ConnectionState.LoggedIn);
		var loggedInPlayers = _sessionState.Count(x => x.Value.State is IConnectionService.ConnectionState.LoggedIn);

		telemetryService?.SetActiveConnectionCount(activeConnections);
		telemetryService?.SetLoggedInPlayerCount(loggedInPlayers);
	}
}