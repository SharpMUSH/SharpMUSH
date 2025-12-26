using System.Collections.Concurrent;
using System.Text;
using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class ConnectionService(
	IPublisher publisher, 
	IConnectionStateStore? stateStore = null,
	ITelemetryService? telemetryService = null) : IConnectionService
{
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

		// Publish notification for Mediator handlers
		await publisher.Publish(new ConnectionStateChangeNotification(get.Handle, get.Ref, get.State,
			IConnectionService.ConnectionState.Disconnected));

		_sessionState.Remove(handle, out _);
		
		// Remove from Redis if available
		if (stateStore != null)
		{
			await stateStore.RemoveConnectionAsync(handle);
		}
		
		// Record disconnection event
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

	public async ValueTask Bind(long handle, DBRef player)
	{
		var get = Get(handle);
		if (get is null) return;

		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during Login."),
			(_, y) => y with { Ref = player, State = IConnectionService.ConnectionState.LoggedIn });

		// Update Redis if available
		if (stateStore != null)
		{
			await stateStore.SetPlayerBindingAsync(handle, player);
		}

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, player, get.State, IConnectionService.ConnectionState.LoggedIn));
		}
		
		// Record login event
		telemetryService?.RecordConnectionEvent("logged_in");
		UpdateConnectionMetrics();

		await publisher.Publish(new ConnectionStateChangeNotification(handle, player, get.State,
			IConnectionService.ConnectionState.LoggedIn));
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

		_sessionState.AddOrUpdate(handle,
			_ => new IConnectionService.ConnectionData(handle, null, IConnectionService.ConnectionState.Connected, 
				outputFunction, promptOutputFunction, encoding, metadata),
			(_, _) => throw new InvalidDataException("Tried to replace an existing handle during Register."));

		// Store in Redis if available
		if (stateStore != null)
		{
			await stateStore.SetConnectionAsync(handle, new ConnectionStateData
			{
				Handle = handle,
				PlayerRef = null,
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

		// Publish notification for Mediator handlers
		await publisher.Publish(new ConnectionStateChangeNotification(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
		
		// Record connection event
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

			// Reconstruct ConnectionData from Redis
			var state = data.State switch
			{
				"LoggedIn" => IConnectionService.ConnectionState.LoggedIn,
				"Connected" => IConnectionService.ConnectionState.Connected,
				_ => IConnectionService.ConnectionState.Connected
			};

			var metadata = new ConcurrentDictionary<string, string>(data.Metadata);

			_sessionState.TryAdd(handle, new IConnectionService.ConnectionData(
				handle,
				data.PlayerRef,
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
		var activeConnections = _sessionState.Count(x => x.Value.State is IConnectionService.ConnectionState.Connected or IConnectionService.ConnectionState.LoggedIn);
		var loggedInPlayers = _sessionState.Count(x => x.Value.State is IConnectionService.ConnectionState.LoggedIn);
		
		telemetryService?.SetActiveConnectionCount(activeConnections);
		telemetryService?.SetLoggedInPlayerCount(loggedInPlayers);
	}
}