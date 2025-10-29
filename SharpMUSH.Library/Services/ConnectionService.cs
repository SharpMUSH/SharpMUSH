using System.Collections.Concurrent;
using System.Text;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class ConnectionService : IConnectionService
{
	private readonly ConcurrentDictionary<long, IConnectionService.ConnectionData> _sessionState = [];
	private readonly List<Action<(long handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState)>> _handlers = [];

	public void Disconnect(long handle)
	{
		var get = Get(handle);
		if (get is null) return;

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(get.Handle, get.Ref, get.State, IConnectionService.ConnectionState.Disconnected));
		}

		_sessionState.Remove(handle, out _);
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

	public void Bind(long handle, DBRef player)
	{
		var get = Get(handle);
		if (get is null) return;

		_sessionState.AddOrUpdate(handle,
			_ => throw new InvalidDataException("Tried to add a new handle during Login."),
			(_, y) => y with { Ref = player, State = IConnectionService.ConnectionState.LoggedIn });

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, player, get.State, IConnectionService.ConnectionState.LoggedIn));
		}
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
	}

	public void Register(long handle, string ipaddr, string host,
		string connectionType,
		Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction, Func<Encoding> encoding,
		ConcurrentDictionary<string, string>? metaData = null)
	{
		_sessionState.AddOrUpdate(handle,
			_ => new IConnectionService.ConnectionData(handle, null, IConnectionService.ConnectionState.Connected, outputFunction, promptOutputFunction, encoding, metaData ??
				new ConcurrentDictionary<string, string>(new Dictionary<string, string> {
					{"ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
					{"LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
					{"InternetProtocolAddress", ipaddr},
					{"HostName", host},
					{"ConnectionType", connectionType}
				})),
			(_, _) => throw new InvalidDataException("Tried to replace an existing handle during Register."));

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
		}
	}
}