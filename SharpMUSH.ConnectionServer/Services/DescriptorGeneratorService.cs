namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Service for generating unique connection descriptors
/// </summary>
public interface IDescriptorGeneratorService
{
	/// <summary>
	/// Get next descriptor for Telnet connections
	/// </summary>
	long GetNextTelnetDescriptor();

	/// <summary>
	/// Get next descriptor for WebSocket connections
	/// </summary>
	long GetNextWebSocketDescriptor();
}

/// <summary>
/// Implementation of descriptor generator service
/// </summary>
public class DescriptorGeneratorService : IDescriptorGeneratorService
{
	private long _telnetCurrent;
	private long _webSocketCurrent;

	public DescriptorGeneratorService(Configuration.ConnectionServerOptions options)
	{
		_telnetCurrent = options.TelnetDescriptorStart;
		_webSocketCurrent = options.WebSocketDescriptorStart;
	}

	public long GetNextTelnetDescriptor()
	{
		return Interlocked.Increment(ref _telnetCurrent);
	}

	public long GetNextWebSocketDescriptor()
	{
		return Interlocked.Increment(ref _webSocketCurrent);
	}
}
