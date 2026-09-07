using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-handle holder of the currently-attached transport. The registered output delegate routes
/// through this, so a session's output can be rebound to a new socket on reconnect, or buffered
/// (replay only) while detached.
/// </summary>
public sealed class SessionSink
{
	private volatile IDuplexTransport? _current;
	private volatile bool _ended;
	public bool Ended { get => _ended; set => _ended = value; }
	public string SessionId { get; set; } = "";
	public DateTimeOffset TokenIssuedAt { get; set; }
	public SemaphoreSlim OutputGate { get; } = new(1, 1);
	public SemaphoreSlim ResumeGate { get; } = new(1, 1);

	public IDuplexTransport? Current => _current;

	public void Attach(IDuplexTransport transport) => _current = transport;

	public void Detach() => _current = null;

	public bool Detach(IDuplexTransport expected) =>
		ReferenceEquals(Interlocked.CompareExchange(ref _current, null, expected), expected);
}
