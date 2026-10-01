using System.Collections.Concurrent;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The per-connection OOB cache. It assumes a single-threaded dispatcher (Blazor WASM): a push, its
/// parse and the events it raises run to completion before the next push, and handlers are not guarded
/// against re-entrancy.
/// </summary>
public sealed class OobChannelStore : IOobChannelStore
{
	private readonly ConcurrentDictionary<string, string> _channels = new();

	// Replaced whole under the lock, read without it: a reader always sees one complete snapshot.
	private readonly Lock _roomLock = new();
	private volatile RoomState _room = RoomState.Empty;

	public event Action<string>? ChannelUpdated;

	public event Action? RoomChanged;

	public RoomState Room => _room;

	public void Set(string package, string dataJson)
	{
		if (string.IsNullOrEmpty(package)) return;
		_channels[package] = dataJson;
		var roomChanged = UpdateRoom(package, dataJson);
		ChannelUpdated?.Invoke(package);
		if (roomChanged) RoomChanged?.Invoke();
	}

	public string? Get(string package) =>
		_channels.TryGetValue(package, out var v) ? v : null;

	public IReadOnlyCollection<string> Packages => _channels.Keys.ToArray();

	public void Clear()
	{
		var cleared = _channels.Keys.ToArray();
		_channels.Clear();
		lock (_roomLock) _room = RoomState.Empty;
		foreach (var package in cleared)
			ChannelUpdated?.Invoke(package);
		RoomChanged?.Invoke();
	}

	/// <summary>Replaces the part of <see cref="Room"/> a room package carries; false for any other package.</summary>
	private bool UpdateRoom(string package, string dataJson)
	{
		switch (package)
		{
			case OobEntryParser.RoomInfoPackage:
				var info = OobEntryParser.ParseRoomInfo(dataJson);
				lock (_roomLock) _room = _room with { Info = info };
				return true;
			case OobEntryParser.RoomContentsPackage:
				var occupants = OobEntryParser.ParseOccupants(dataJson);
				lock (_roomLock) _room = _room with { Occupants = occupants };
				return true;
			case OobEntryParser.RoomExitsPackage:
				var exits = OobEntryParser.ParseExits(dataJson);
				lock (_roomLock) _room = _room with { Exits = exits };
				return true;
			default:
				return false;
		}
	}
}
