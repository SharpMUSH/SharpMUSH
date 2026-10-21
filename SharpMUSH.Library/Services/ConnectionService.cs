using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;

namespace SharpMUSH.Library.Services;

public class ConnectionService(
	IPublisher publisher,
	IConnectionStateStore? stateStore = null,
	ITelemetryService? telemetryService = null) : IConnectionService
{
	/// <summary>
	/// Identifies restored legacy entries that lack an explicit transport incarnation.
	/// </summary>
	private const string ReconciledMarker = "ReconciledFromStateStore";

	private readonly ConcurrentDictionary<long, IConnectionService.ConnectionData> _sessionState = [];
	/// <summary>
	/// Replaced wholesale on every registration, so each notification loops over the snapshot it started
	/// with. Listeners register late and from any thread — <c>InputSessionService</c> does it in its
	/// constructor, and a listener may register another while it is being notified — so no notification
	/// can be left enumerating a collection that changes under it.
	/// </summary>
	private ImmutableArray<Action<(long handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState)>> _handlers = [];

	/// <summary>
	/// Each player's handles, so a lookup by player reads only that player's connections instead of
	/// scanning every one. <c>idle()</c> and <c>conn()</c> look a player up on each call, which made a
	/// listing of every player cost connections squared. Written only under <see cref="_bindingLock"/>;
	/// read without it.
	/// </summary>
	private readonly ConcurrentDictionary<DBRef, ImmutableHashSet<long>> _handlesByPlayer = [];

	/// <summary>
	/// Held around every change that adds a handle, removes one or changes the player it is bound to,
	/// together with the matching change to <see cref="_handlesByPlayer"/>, so the index never disagrees
	/// with <see cref="_sessionState"/>. It also makes <see cref="Disconnect"/>'s remove-and-count atomic:
	/// whichever of two of a player's handles disconnecting at the same time runs second sees the first
	/// one already gone, never as "still connected."
	/// </summary>
	private readonly Lock _bindingLock = new();

	public async ValueTask Disconnect(long handle, string? sessionId = null)
	{
		var get = Get(handle);
		if (get is null || (!string.IsNullOrEmpty(sessionId) && get.Metadata.GetValueOrDefault("SessionId") != sessionId)) return;

		// A character binding may change while a private send holds its gate. The close still
		// belongs to this transport, so retry on its new binding rather than losing the close.
		// A replacement socket (or a changed SessionId) must never be removed by that retry.
		var transportMetadata = get.Metadata;
		var transportSession = string.IsNullOrEmpty(sessionId) ? get.Metadata.GetValueOrDefault("SessionId") : sessionId;
		int? remainingConnections;
		while (true)
		{
			var binding = get.Binding;
			await binding.PublicationGate.WaitAsync();
			try
			{
				lock (_bindingLock)
				{
					var current = Get(handle);
					if (current is null || !ReferenceEquals(current.Metadata, transportMetadata)
						|| current.Metadata.GetValueOrDefault("SessionId") != transportSession) return;
					get = current;
					if (!ReferenceEquals(current.Binding, binding)) continue;
					if (!_sessionState.TryRemove(new KeyValuePair<long, IConnectionService.ConnectionData>(handle, current))) continue;
					Reindex(handle, current.Ref, null);

					remainingConnections = current.Ref is { } playerRef
						? _handlesByPlayer.GetValueOrDefault(playerRef, []).Count(h => Get(h)?.PresenceClass != PresenceClasses.Portal)
						: null;
					break;
				}
			}
			finally { binding.PublicationGate.Release(); }
		}

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(get.Handle, get.Ref, get.State, IConnectionService.ConnectionState.Disconnected));
		}

		await publisher.Publish(new ConnectionStateChangeNotification(get.Handle, get.Ref, get.State,
			IConnectionService.ConnectionState.Disconnected, RemainingConnections: remainingConnections, FormerConnection: get));

		// The socket owner already deleted a fenced close. A second delete could erase its replacement.
		if (stateStore != null && string.IsNullOrEmpty(sessionId))
		{
			await stateStore.RemoveConnectionAsync(handle);
		}

		telemetryService?.RecordConnectionEvent("disconnected");
		UpdateConnectionMetrics();
	}

	public IConnectionService.ConnectionData? Get(long handle) =>
		_sessionState.GetValueOrDefault(handle);

	// The handle set is read without the lock, so a handle may have been rebound since; the Ref check
	// drops it rather than answering for the wrong player.
	public IAsyncEnumerable<IConnectionService.ConnectionData> Get(DBRef reference) =>
		_handlesByPlayer.GetValueOrDefault(reference, [])
			.Select(Get)
			.OfType<IConnectionService.ConnectionData>()
			.Where(x => x.Ref == reference)
			.ToAsyncEnumerable();

	// Enumerates the dictionary itself. Its Values property takes every bucket lock and copies the
	// whole table on each call; the enumerator does neither. It is not a point-in-time snapshot, which
	// nothing here relied on: each caller re-checks a connection it acts on.
	public IAsyncEnumerable<IConnectionService.ConnectionData> GetAll() =>
		_sessionState
			.Select(x => x.Value)
			.ToAsyncEnumerable();

	public async ValueTask<bool> IsPlayerHiddenAsync(DBRef playerRef)
		=> await Get(playerRef).AnyAsync(conn => conn.IsHidden);

	public void ListenState(Action<(long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler) =>
		ImmutableInterlocked.Update(ref _handlers, handlers => handlers.Add(handler));

	public async ValueTask Bind(long handle, DBRef player, bool firstLogin = false)
	{
		await BindCoreAsync(handle, player, firstLogin, null);
	}

	public ValueTask<bool> BindAuthorizedAsync(long handle, DBRef player, Func<Task<bool>> authorize, Action? onCommitted = null)
		=> BindCoreAsync(handle, player, false, authorize, onCommitted);

	private async ValueTask<bool> BindCoreAsync(long handle, DBRef player, bool firstLogin, Func<Task<bool>>? authorize, Action? onCommitted = null)
	{
		var get = Get(handle);
		if (get is null || authorize is not null && get.Ref is not null) return false;

		if (get.Ref is not null && get.Ref != player && !await RevokeResumeAsync(get)) return false;
		if (!await ReplaceAsync(get, get with { Ref = player, State = IConnectionService.ConnectionState.LoggedIn }, authorize)) return false;
		// The local binding and player index are committed before any fallible notification work.
		onCommitted?.Invoke();

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
		return true;
	}

	public async ValueTask Unbind(long handle)
	{
		var get = Get(handle);
		if (get is null || get.Ref is null) return;

		if (!await RevokeResumeAsync(get)) return;
		var formerRef = get.Ref;

		// State is updated before the notification is published, so a PLAYER`DISCONNECT handler asking
		// for the player's remaining connections does not count the one that is leaving.
		if (!await ReplaceAsync(get, get with { Ref = null, State = IConnectionService.ConnectionState.Connected })) return;

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

		// PennMUSH's logout_sock explicitly resets d->hide = 0 (bsd.c:2248) - without this, a wizard who
		// @hides then LOGOUTs would leave the socket hidden for whoever connects next on it, including a
		// mortal with no permission to hide themselves. This clear runs AFTER the notification publish
		// (rather than folded into the AddOrUpdate above) so a PLAYER`DISCONNECT handler reading
		// Get(handle).IsHidden while handling that notification still observes the pre-logout Hidden
		// value for its "hidden?" argument and for ConnectionAnnounceService's disconnect wording -
		// clearing it beforehand made every LOGOUT (as opposed to QUIT) report as an ordinary,
		// non-hidden disconnect regardless of the player's actual Hidden state.
		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during Logout."),
			(_, y) =>
			{
				y.Metadata.TryRemove("Hidden", out var removedHiddenValue);
				return y;
			});

		if (stateStore != null)
		{
			await stateStore.UpdateMetadataAsync(handle, "Hidden", "0");
		}
	}

	public async ValueTask BindAccount(long handle, string accountId)
	{
		var get = Get(handle);
		if (get is null) return;

		if ((get.Ref is not null || (get.State == IConnectionService.ConnectionState.AccountMode &&
			get.Metadata.GetValueOrDefault("AccountId") != accountId)) && !await RevokeResumeAsync(get)) return;
		var oldState = get.State;
		if (!await ReplaceAsync(get, get with { Ref = null, State = IConnectionService.ConnectionState.AccountMode })) return;
		get.Metadata["AccountId"] = accountId;

		if (stateStore != null)
		{
			if (get.Ref is not null) await stateStore.SetPlayerBindingAsync(handle, null);
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

	/// <summary>Swaps <paramref name="current"/> for <paramref name="next"/> if it is still the stored entry.</summary>
	private async ValueTask<bool> ReplaceAsync(IConnectionService.ConnectionData current, IConnectionService.ConnectionData next, Func<Task<bool>>? authorize = null)
	{
		await current.Binding.PublicationGate.WaitAsync();
		try
		{
			if (authorize is not null && !await authorize()) return false;
			lock (_bindingLock)
			{
				if (!_sessionState.TryUpdate(current.Handle, next with { Binding = new() }, current)) return false;
				Reindex(current.Handle, current.Ref, next.Ref);
				return true;
			}
		}
		finally { current.Binding.PublicationGate.Release(); }
	}

	/// <summary>Moves <paramref name="handle"/> from <paramref name="from"/>'s set to <paramref name="to"/>'s. Caller holds <see cref="_bindingLock"/>.</summary>
	private void Reindex(long handle, DBRef? from, DBRef? to)
	{
		if (from == to) return;

		if (from is { } former && _handlesByPlayer.TryGetValue(former, out var formerHandles))
		{
			var rest = formerHandles.Remove(handle);
			if (rest.IsEmpty) _handlesByPlayer.TryRemove(former, out _);
			else _handlesByPlayer[former] = rest;
		}

		if (to is { } player)
		{
			_handlesByPlayer[player] = _handlesByPlayer.GetValueOrDefault(player, []).Add(handle);
		}
	}

	private async Task<bool> RevokeResumeAsync(IConnectionService.ConnectionData connection)
	{
		if (connection.Metadata.GetValueOrDefault("ResumeRevoked") == "1") return true;
		// Legacy connections have no resumable session identity. For resumable connections,
		// fence the first KV read as well as every CAS retry to the caller's session.
		var sessionId = connection.Metadata.GetValueOrDefault("SessionId");
		if (stateStore is not null && !string.IsNullOrEmpty(sessionId)
			&& !await stateStore.TryRevokeResumeAsync(connection.Handle, sessionId)) return false;
		connection.Metadata["ResumeRevoked"] = "1";
		return true;
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

		// Persist noncritical metadata asynchronously; authentication changes are awaited above.
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
					// Best-effort metadata must not fault the detached task; durable binding changes propagate failures.
				}
			});
		}
	}

	public void Update(long handle, string key, Func<string?, string> change)
	{
		if (Get(handle) is null) return;

		string? newValue = null;
		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during update."),
			(_, y) =>
			{
				newValue = y.Metadata.AddOrUpdate(key, _ => change(null), (_, existing) => change(existing));
				return y;
			});

		// Persist noncritical metadata asynchronously; authentication changes are awaited above.
		if (stateStore is null || newValue is null) return;

		var captured = newValue;
		_ = Task.Run(async () =>
		{
			try
			{
				await stateStore.UpdateMetadataAsync(handle, key, captured);
			}
			catch
			{
				// Best-effort metadata must not fault the detached task; durable binding changes propagate failures.
			}
		});
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

		// Persist noncritical metadata asynchronously; authentication changes are awaited above.
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
					// Best-effort metadata must not fault the detached task; durable binding changes propagate failures.
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

		// Redelivery keeps the same incarnation, including after engine reconciliation. A new
		// socket must never inherit a previous occupant's login; delayed old registrations lose.
		IConnectionService.ConnectionData? replaced = null;
		lock (_bindingLock)
		{
			var stored = _sessionState.AddOrUpdate(handle, newEntry, (_, existing) =>
			{
				replaced = existing;
				return ShouldReplaceRegistration(existing, newEntry) ? newEntry : existing;
			});

			if (!ReferenceEquals(stored, newEntry)) return;
			// A restored entry it replaces may have been bound; the new socket is not.
			Reindex(handle, replaced?.Ref, null);
		}

		if (stateStore != null && string.IsNullOrEmpty(metadata.GetValueOrDefault("SessionId")))
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

	private static bool ShouldReplaceRegistration(IConnectionService.ConnectionData existing,
		IConnectionService.ConnectionData incoming)
	{
		var oldSession = existing.Metadata.GetValueOrDefault("SessionId");
		var newSession = incoming.Metadata.GetValueOrDefault("SessionId");
		if (!string.IsNullOrEmpty(newSession))
		{
			if (newSession == oldSession) return false;
			return RegistrationTime(incoming) > RegistrationTime(existing);
		}
		return string.IsNullOrEmpty(oldSession) && existing.Metadata.ContainsKey(ReconciledMarker);
	}

	private static long RegistrationTime(IConnectionService.ConnectionData connection)
	{
		if (long.TryParse(connection.Metadata.GetValueOrDefault("ConnectionIncarnationTime"), out var ticks)) return ticks;
		return long.TryParse(connection.Metadata.GetValueOrDefault("ConnectionStartTime"), out var milliseconds)
			? milliseconds * TimeSpan.TicksPerMillisecond : 0;
	}

	/// <summary>
	/// Reconcile state from the shared connection store on startup.
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

			var player = data.PlayerObjid is null ? null : (DBRef?)DBRef.Parse(data.PlayerObjid);
			lock (_bindingLock)
			{
				if (!_sessionState.TryAdd(handle, new IConnectionService.ConnectionData(
					handle,
					player,
					state,
					createOutputFunction(handle),
					createPromptOutputFunction(handle),
					encodingFunction,
					metadata
				))) continue;
				Reindex(handle, null, player);
			}
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