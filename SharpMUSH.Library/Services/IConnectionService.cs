using SharpMUSH.Library.Models;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Library.Services;

public interface IConnectionService
{
	enum ConnectionState { Error, None, Connected, LoggedIn, Disconnected }
	
	public record ConnectionData(
		string Handle, 
		DBRef? Ref, 
		ConnectionState State, 
		Func<byte[], ValueTask> OutputFunction, 
		Func<Encoding> Encoding,
		ConcurrentDictionary<string,string> Metadata
	);

	void Register(string handle, Func<byte[], ValueTask> outputFunction, Func<Encoding> encoding, ConcurrentDictionary<string, string>? MetaData = null);
		
	void Bind(string handle, DBRef player);

	void Update(string handle, string key, string value);

	void Disconnect(string handle);

	/// <summary>
	/// Gets the connection state of a handle.
	/// </summary>
	/// <param name="handle"></param>
	ConnectionData? Get(string handle);

	/// <summary>
	/// Get all handles connected to the DBRef
	/// </summary>
	/// <param name="reference">A database reference</param>
	/// <returns>All matching handles connected to the DBRef</returns>
	IEnumerable<ConnectionData> Get(DBRef reference);

	/// <summary>
	/// Gets all handle information.
	/// </summary>
	IEnumerable<ConnectionData> GetAll();

	/// <summary>
	/// Register a handler that listens to connection change events.
	/// </summary>
	/// <param name="handler">A handling function.</param>
	void ListenState(Action<(string, DBRef?, ConnectionState, ConnectionState)> handler);
}