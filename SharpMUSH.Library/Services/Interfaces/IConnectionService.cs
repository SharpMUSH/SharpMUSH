using System.Collections.Concurrent;
using System.Text;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IConnectionService
{
	enum ConnectionState
	{
		Error,
		None,
		Connected,
		LoggedIn,
		Disconnected
	}

	record ConnectionData(
		long Handle,
		DBRef? Ref,
		ConnectionState State,
		Func<byte[], ValueTask> OutputFunction,
		Func<byte[], ValueTask> PromptOutputFunction,
		Func<Encoding> Encoding,
		ConcurrentDictionary<string, string> Metadata
	)
	{
		public TimeSpan? Connected 
			=> State is ConnectionState.Connected or ConnectionState.LoggedIn
			? DateTimeOffset.UtcNow -
			  DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Metadata["ConnectionStartTime"]))
			: null;

		public TimeSpan? Idle 
			=> State is ConnectionState.Connected or ConnectionState.LoggedIn
			? DateTimeOffset.UtcNow -
			  DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Metadata["LastConnectionSignal"]))
			: null;
		
		public string InternetProtocolAddress => Metadata.GetValueOrDefault(nameof(InternetProtocolAddress), "UNKNOWN");

		public string HostName => Metadata.GetValueOrDefault(nameof(HostName), InternetProtocolAddress);
		
		public string ConnectionType => Metadata[nameof(ConnectionType)];
	}

	void Register(long handle, string ipaddr, string host, string connectionType, Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction, Func<Encoding> encoding,
		ConcurrentDictionary<string, string>? metaData = null);

	void Bind(long handle, DBRef player);

	void Update(long handle, string key, string value);

	void Disconnect(long handle);

	/// <summary>
	/// Gets the connection state of a handle.
	/// </summary>
	/// <param name="handle"></param>
	ConnectionData? Get(long handle);
	
	/// <summary>
	/// Get all handles connected to the DBRef
	/// </summary>
	/// <param name="reference">A database reference</param>
	/// <returns>All matching handles connected to the DBRef</returns>
	IAsyncEnumerable<ConnectionData> Get(DBRef reference);

	/// <summary>
	/// Gets all handle information.
	/// </summary>
	IAsyncEnumerable<ConnectionData> GetAll();

	/// <summary>
	/// Register a handler that listens to connection change events.
	/// </summary>
	/// <param name="handler">A handling function.</param>
	void ListenState(Action<(long, DBRef?, ConnectionState, ConnectionState)> handler);
}