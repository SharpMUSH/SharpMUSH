using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface IConnectionService
	{
		enum ConnectionState { Error, None, Connected, LoggedIn, Disconnected };

		void Register(int handle);

		void Login(int handle, DBRef player);

		void Disconnect(int handle);

		/// <summary>
		/// Gets the connection state of a handle.
		/// </summary>
		/// <param name="handle"></param>
		(int, DBRef?, ConnectionState)? Get(int handle);

		/// <summary>
		/// Gets all handle information.
		/// </summary>
		IEnumerable<(int,DBRef?,ConnectionState)> GetAll();

		/// <summary>
		/// Register a handler that listens to connection change events.
		/// </summary>
		/// <param name="handler">A handling function.</param>
		void ListenState(Action<(int, DBRef?, ConnectionState, ConnectionState)> handler);
	}
}