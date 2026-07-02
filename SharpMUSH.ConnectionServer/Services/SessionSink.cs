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

	public IDuplexTransport? Current => _current;

	public void Attach(IDuplexTransport transport) => _current = transport;

	public void Detach() => _current = null;
}
