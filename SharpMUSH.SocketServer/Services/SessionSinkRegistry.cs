using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Maps a handle to its <see cref="SessionSink"/> so a reconnecting pump can rebind it.</summary>
public sealed class SessionSinkRegistry
{
	private readonly ConcurrentDictionary<long, SessionSink> _sinks = new();

	public SessionSink GetOrCreate(long handle) => _sinks.GetOrAdd(handle, _ => new SessionSink());

	public SessionSink? Get(long handle) => _sinks.TryGetValue(handle, out var s) ? s : null;

	public void Remove(long handle) => _sinks.TryRemove(handle, out _);
}
