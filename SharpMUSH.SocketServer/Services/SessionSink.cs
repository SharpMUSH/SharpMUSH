using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-handle holder of the currently-attached transport. The registered output delegate routes
/// through this, so a session's output can be rebound to a new socket on reconnect, or buffered
/// (replay only) while detached.
/// </summary>
public sealed class SessionSink
{
	private readonly object _lifetimeGate = new();
	private volatile IDuplexTransport? _current;
	private volatile bool _ended;
	public bool Ended => _ended;
	public string SessionId { get; set; } = "";
	public DateTimeOffset TokenIssuedAt { get; set; }
	public SemaphoreSlim OutputGate { get; } = new(1, 1);
	public SemaphoreSlim ResumeGate { get; } = new(1, 1);

	public IDuplexTransport? Current => _current;

	public void Attach(IDuplexTransport transport)
	{
		if (!TryAttach(transport)) throw new InvalidOperationException("The session has ended.");
	}

	public bool TryAttach(IDuplexTransport transport)
	{
		lock (_lifetimeGate)
		{
			if (_ended) return false;
			_current = transport;
			return true;
		}
	}

	public IDuplexTransport? End()
	{
		lock (_lifetimeGate)
		{
			_ended = true;
			return Interlocked.Exchange(ref _current, null);
		}
	}

	public void Detach() => _current = null;

	public bool Detach(IDuplexTransport expected) =>
		ReferenceEquals(Interlocked.CompareExchange(ref _current, null, expected), expected);
}
