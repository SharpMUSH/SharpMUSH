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
	/// Identifies restored legacy entries that lack an explicit transport incarnation.
	/// </summary>
	private const string ReconciledMarker = "ReconciledFromStateStore";

	private readonly ConcurrentDictionary<long, IConnectionService.ConnectionData> _sessionState = [];
	private readonly List<Action<(long handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState)>> _handlers = [];

	/// <summary>
	/// Guards <see cref="Disconnect"/>'s atomic remove-and-count against a race between two of the same
	/// player's handles disconnecting at genuinely the same time. Both the removal from
	/// <see cref="_sessionState"/> and the subsequent count of the player's remaining connections happen
	/// inside this lock, so whichever of two concurrent <see cref="Disconnect"/> calls for the same
	/// player runs second always sees the first one's handle already gone, never as "still connected."
	/// </summary>
	private readonly Lock _disconnectLock = new();

	public async ValueTask Disconnect(long handle, string? sessionId = null)
	{
		var get = Get(handle);
		if (get is null || (!string.IsNullOrEmpty(sessionId) && get.Metadata.GetValueOrDefault("SessionId") != sessionId)) return;

		int? remainingConnections;
		lock (_disconnectLock)
		{
			if (!_sessionState.TryRemove(new KeyValuePair<long, IConnectionService.ConnectionData>(handle, get))) return;

			remainingConnections = get.Ref is { } playerRef
				? _sessionState.Values.Count(x => x.Ref.HasValue && x.Ref.Value.Equals(playerRef))
				: null;
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

	public IAsyncEnumerable<IConnectionService.ConnectionData> Get(DBRef reference) =>
		_sessionState.Values
			.ToAsyncEnumerable()
			.Where(x => x.Ref.HasValue)
			.Where(x => x.Ref!.Value.Equals(reference));

	public IAsyncEnumerable<IConnectionService.ConnectionData> GetAll() =>
		_sessionState.Values
			.ToAsyncEnumerable();

	public async ValueTask<bool> IsPlayerHiddenAsync(DBRef playerRef)
	{
		await foreach (var conn in Get(playerRef))
		{
			if (conn.IsHidden)
			{
				return true;
			}
		}
		return false;
	}

	public void ListenState(Action<(long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler) =>
		_handlers.Add(handler);

	public async ValueTask Bind(long handle, DBRef player, bool firstLogin = false)
	{
		var get = Get(handle);
		if (get is null) return;

		if (get.Ref is not null && get.Ref != player && !await RevokeResumeAsync(get)) return;
		if (!_sessionState.TryUpdate(handle, get with { Ref = player, State = IConnectionService.ConnectionState.LoggedIn }, get)) return;

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

		if (!await RevokeResumeAsync(get)) return;
		var formerRef = get.Ref;

		// State is updated before the notification is published, so a PLAYER`DISCONNECT handler asking
		// for the player's remaining connections does not count the one that is leaving.
		if (!_sessionState.TryUpdate(handle, get with { Ref = null, State = IConnectionService.ConnectionState.Connected }, get)) return;

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
		if (!_sessionState.TryUpdate(handle, get with { Ref = null, State = IConnectionService.ConnectionState.AccountMode }, get)) return;
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
		var stored = _sessionState.AddOrUpdate(handle, newEntry, (_, existing) =>
			ShouldReplaceRegistration(existing, newEntry) ? newEntry : existing);

		if (!ReferenceEquals(stored, newEntry)) return;

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