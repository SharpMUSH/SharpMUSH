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

	ValueTask<long> GetNextTelnetDescriptorAsync(CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(GetNextTelnetDescriptor());
	}

	ValueTask<long> GetNextWebSocketDescriptorAsync(CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return ValueTask.FromResult(GetNextWebSocketDescriptor());
	}

	void ReserveWebSocketDescriptor(long descriptor);

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
	private readonly NextUnoccupiedNumberGenerator _telnetGenerator;
	private readonly NextUnoccupiedNumberGenerator _webSocketGenerator;
	private readonly object _lock = new();
	private readonly HashSet<long> _occupiedWebSockets = [];

	public DescriptorGeneratorService(Configuration.ConnectionServerOptions options)
	{
		_telnetGenerator = new NextUnoccupiedNumberGenerator(options.TelnetDescriptorStart + 1);
		_webSocketGenerator = new NextUnoccupiedNumberGenerator(options.WebSocketDescriptorStart + 1);
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
			var next = _webSocketGenerator.Get().First();
			while (_occupiedWebSockets.Contains(next)) next = _webSocketGenerator.Get().First();
			_occupiedWebSockets.Add(next);
			return next;
		}
	}

	public void ReserveWebSocketDescriptor(long descriptor)
	{
		lock (_lock) _occupiedWebSockets.Add(descriptor);
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
			if (_occupiedWebSockets.Remove(descriptor)) _webSocketGenerator.Release(descriptor);
		}
	}
}
