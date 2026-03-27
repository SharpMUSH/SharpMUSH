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

	/// <summary>
	/// Release a previously allocated Telnet descriptor so it can be reused
	/// </summary>
	void ReleaseTelnetDescriptor(long descriptor);

	/// <summary>
	/// Release a previously allocated WebSocket descriptor so it can be reused
	/// </summary>
	void ReleaseWebSocketDescriptor(long descriptor);
}

/// <summary>
/// Implementation of descriptor generator service
/// </summary>
public class DescriptorGeneratorService : IDescriptorGeneratorService
{
	private readonly Library.Services.NextUnoccupiedNumberGenerator _telnetGenerator;
	private readonly Library.Services.NextUnoccupiedNumberGenerator _webSocketGenerator;
	private readonly object _lock = new();

	public DescriptorGeneratorService(Configuration.ConnectionServerOptions options)
	{
		_telnetGenerator = new Library.Services.NextUnoccupiedNumberGenerator(options.TelnetDescriptorStart + 1);
		_webSocketGenerator = new Library.Services.NextUnoccupiedNumberGenerator(options.WebSocketDescriptorStart + 1);
	}

	public long GetNextTelnetDescriptor()
	{
		lock (_lock)
		{
			return _telnetGenerator.Get().First();
		}
	}

	public long GetNextWebSocketDescriptor()
	{
		lock (_lock)
		{
			return _webSocketGenerator.Get().First();
		}
	}

	public void ReleaseTelnetDescriptor(long descriptor)
	{
		lock (_lock)
		{
			_telnetGenerator.Release(descriptor);
		}
	}

	public void ReleaseWebSocketDescriptor(long descriptor)
	{
		lock (_lock)
		{
			_webSocketGenerator.Release(descriptor);
		}
	}
}
