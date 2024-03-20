using SharpMUSH.Library.Models;
using System.Collections.Concurrent;

namespace SharpMUSH.Library.Services
{
	public class ConnectionService : IConnectionService
	{
		private readonly ConcurrentDictionary<string, (string Handle, DBRef? Ref, IConnectionService.ConnectionState State, Func<byte[], Task> OutputFunction)> _sessionState = [];
		private readonly List<Action<(string Handle, DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState)>> _handlers = [];

		public void Disconnect(string handle) {
			var get = Get(handle);
			if (get == null) return;

			foreach(var handler in _handlers)
			{
				handler(new(get.Value.Item1, get.Value.Item2, get.Value.Item3, IConnectionService.ConnectionState.Disconnected));
			}

			_sessionState.Remove(handle, out _);
		}

		public (string, DBRef?, IConnectionService.ConnectionState, Func<byte[], Task>)? Get(string handle) =>
			_sessionState.GetValueOrDefault(handle);

		public IEnumerable<(string, DBRef?, IConnectionService.ConnectionState, Func<byte[], Task>)> Get(DBRef reference) =>
			_sessionState.Values.Where(x => x.Ref.HasValue).Where(x => x.Ref!.Value.Equals(reference));

		public IEnumerable<(string, DBRef?, IConnectionService.ConnectionState, Func<byte[], Task>)> GetAll() =>
			_sessionState.Values;

		public void ListenState(Action<(string, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler) =>
			_handlers.Add(handler);

		public void Bind(string handle, DBRef player)
		{
			var get = Get(handle);
			if (get == null) return;
			
			_sessionState.AddOrUpdate(handle, 
				x => throw new InvalidDataException("Tried to add a new handle during Login."), 
				(x,y) => (y.Handle, player, IConnectionService.ConnectionState.LoggedIn, y.OutputFunction));

			foreach (var handler in _handlers)
			{
				handler(new(handle, player, get.Value.Item3, IConnectionService.ConnectionState.LoggedIn));
			}
		}

		public void Register(string handle, Func<byte[],Task> outputFunction)
		{
			_sessionState.AddOrUpdate(handle,
				x => (handle, null, IConnectionService.ConnectionState.Connected, outputFunction),
				(x, y) => (handle, null, IConnectionService.ConnectionState.Connected, outputFunction));

			foreach (var handler in _handlers)
			{
				handler(new(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
			}
		}
	}
}