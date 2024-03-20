using SharpMUSH.Library.Models;
using System.Collections.Concurrent;

namespace SharpMUSH.Library.Services
{
	public class ConnectionService : IConnectionService
	{
		private readonly ConcurrentDictionary<string, (string Handle, DBRef? Ref, IConnectionService.ConnectionState State)> _sessionState = [];
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

		public (string, DBRef?, IConnectionService.ConnectionState)? Get(string handle) =>
			_sessionState.GetValueOrDefault(handle);

		public IEnumerable<(string, DBRef?, IConnectionService.ConnectionState)> Get(DBRef reference) =>
			_sessionState.Values.Where(x => x.Ref.HasValue).Where(x => x.Ref!.Value.Equals(reference));

		public IEnumerable<(string, DBRef?, IConnectionService.ConnectionState)> GetAll() =>
			_sessionState.Values;

		public void ListenState(Action<(string, DBRef?, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler) =>
			_handlers.Add(handler);

		public void ListenState(Action<(string, DBRef, IConnectionService.ConnectionState, IConnectionService.ConnectionState)> handler)
		{
			throw new NotImplementedException();
		}

		public void Login(string handle, DBRef player)
		{
			var get = Get(handle);
			if (get == null) return;
			
			_sessionState.AddOrUpdate(handle, 
				x => (handle, player, IConnectionService.ConnectionState.LoggedIn), 
				(x,y) => (handle, player, IConnectionService.ConnectionState.LoggedIn));

			foreach (var handler in _handlers)
			{
				handler(new(handle, player, get.Value.Item3, IConnectionService.ConnectionState.LoggedIn));
			}
		}

		public void Register(string handle)
		{
			_sessionState.AddOrUpdate(handle,
				x => (handle, null, IConnectionService.ConnectionState.Connected),
				(x, y) => (handle, null, IConnectionService.ConnectionState.Connected));

			foreach (var handler in _handlers)
			{
				handler(new(handle, null, IConnectionService.ConnectionState.None, IConnectionService.ConnectionState.Connected));
			}
		}
	}
}