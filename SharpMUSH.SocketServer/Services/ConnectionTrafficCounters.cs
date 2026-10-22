using System.Collections.Concurrent;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The bytes each connection has moved, which <c>SESSION</c> asks for. A telnet connection counts from
/// its first byte; a WebSocket one from registration, and through every socket it is resumed on.
/// </summary>
public sealed class ConnectionTrafficCounters
{
	private readonly ConcurrentDictionary<long, TrafficCounter> _counters = new();

	/// <summary>A new count for <paramref name="handle"/>, replacing whatever a previous holder of the handle left.</summary>
	public TrafficCounter Start(long handle)
	{
		var counter = new TrafficCounter();
		_counters[handle] = counter;
		return counter;
	}

	/// <summary>The count for <paramref name="handle"/>, started if it has none.</summary>
	public TrafficCounter For(long handle) => _counters.GetOrAdd(handle, static _ => new TrafficCounter());

	/// <summary>Drops <paramref name="handle"/>'s count, unless a newer holder of the handle has started its own.</summary>
	public void End(long handle, TrafficCounter counter) =>
		_counters.TryRemove(new KeyValuePair<long, TrafficCounter>(handle, counter));

	/// <summary>Drops <paramref name="handle"/>'s count.</summary>
	public void End(long handle) => _counters.TryRemove(handle, out _);

	/// <summary>Every connection's count as it stands.</summary>
	public ConnectionTraffic[] Snapshot() =>
		[.. _counters.Select(entry => entry.Value.Read(entry.Key))];
}

/// <summary>One connection's byte counts. Every member is safe to call from any thread.</summary>
public sealed class TrafficCounter
{
	private long _received;
	private long _sent;
	private long _pending;

	/// <summary>The client sent <paramref name="bytes"/>.</summary>
	public void Received(long bytes) => Interlocked.Add(ref _received, bytes);

	/// <summary><paramref name="bytes"/> were handed to the socket and are waiting to go.</summary>
	public void Writing(long bytes) => Interlocked.Add(ref _pending, bytes);

	/// <summary><paramref name="bytes"/> that were waiting have gone.</summary>
	public void Written(long bytes)
	{
		Interlocked.Add(ref _pending, -bytes);
		Interlocked.Add(ref _sent, bytes);
	}

	/// <summary><paramref name="bytes"/> that were waiting will not go: the write failed.</summary>
	public void Dropped(long bytes) => Interlocked.Add(ref _pending, -bytes);

	public ConnectionTraffic Read(long handle) =>
		new(handle, Interlocked.Read(ref _received), Interlocked.Read(ref _sent), Interlocked.Read(ref _pending));
}
