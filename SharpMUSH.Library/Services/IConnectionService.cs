using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface IConnectionService
	{
		enum ConnectionState { Error, None, Connected, LoggedIn, Disconnected };

		void Register(string handle);

		void Login(string handle, DBRef player);

		void Disconnect(string handle);

		/// <summary>
		/// Gets the connection state of a handle.
		/// </summary>
		/// <param name="handle"></param>
		(string, DBRef?, ConnectionState)? Get(string handle);

		/// <summary>
		/// Get all handles connected to the DBRef
		/// </summary>
		/// <param name="reference">A database reference</param>
		/// <returns>All matching handles connected to the DBRef</returns>
		IEnumerable<(string, DBRef?, ConnectionState)> Get(DBRef reference);

		/// <summary>
		/// Gets all handle information.
		/// </summary>
		IEnumerable<(string, DBRef?,ConnectionState)> GetAll();

		/// <summary>
		/// Register a handler that listens to connection change events.
		/// </summary>
		/// <param name="handler">A handling function.</param>
		void ListenState(Action<(string, DBRef?, ConnectionState, ConnectionState)> handler);
	}
}