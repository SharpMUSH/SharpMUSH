using System.Collections.Concurrent;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// How to ask each telnet connection's terminal what it can draw. The question has to go out through
/// that connection's own interpreter, which only the connection's handler holds, so the handler leaves a
/// way to ask here for as long as the connection is open.
/// </summary>
public sealed class TerminalProbes
{
	private readonly ConcurrentDictionary<long, Func<ValueTask>> _probes = new();

	/// <summary>Records how to ask the terminal on <paramref name="handle"/>.</summary>
	public void Register(long handle, Func<ValueTask> probe) => _probes[handle] = probe;

	/// <summary>Forgets <paramref name="handle"/>, once its connection has closed.</summary>
	public void Unregister(long handle) => _probes.TryRemove(handle, out _);

	/// <summary>Asks the terminal on <paramref name="handle"/>; false when no telnet connection has that handle.</summary>
	public async ValueTask<bool> ProbeAsync(long handle)
	{
		if (!_probes.TryGetValue(handle, out var probe)) return false;
		await probe();
		return true;
	}
}
