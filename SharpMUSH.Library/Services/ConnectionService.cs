﻿using SharpMUSH.Library.Models;
using System.Collections.Concurrent;
using System.Text;

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
	
	public IEnumerable<IConnectionService.ConnectionData> Get(DBRef reference) =>
		_sessionState.Values.Where(x => x.Ref.HasValue).Where(x => x.Ref!.Value.Equals(reference));

	public IEnumerable<IConnectionService.ConnectionData> GetAll() =>
		_sessionState.Values;

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

	public void Register(long handle, Func<byte[], ValueTask> outputFunction, Func<Encoding> encoding, ConcurrentDictionary<string, string>? metaData = null)
	{
		_sessionState.AddOrUpdate(handle,
			_ => new IConnectionService.ConnectionData(handle, null, IConnectionService.ConnectionState.Connected, outputFunction, encoding, metaData ??
				new ConcurrentDictionary<string, string>(new Dictionary<string, string> {
					{"ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
					{"LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }})),
			(_, _) => throw new InvalidDataException("Tried to replace an existing handle during Register."));

		foreach (var handler in _handlers)
		{
			handler(new ValueTuple<long, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState>(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
		}
	}
}