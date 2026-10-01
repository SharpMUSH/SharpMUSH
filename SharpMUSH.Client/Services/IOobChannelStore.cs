using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A per-connection cache of the latest payload for each OOB package/channel. The raw JSON of every
/// package is kept as sent; the three <c>room.*</c> packages the <c>room-contents</c> softcode pushes are
/// also kept typed, in <see cref="Room"/>, so each consumer does not parse them again.
/// </summary>
public interface IOobChannelStore
{
	/// <summary>Raised after a package's payload is stored (or cleared), with the package name.</summary>
	event Action<string>? ChannelUpdated;

	/// <summary>
	/// Raised after <see cref="Room"/> is replaced: a <c>room.info</c>, <c>room.contents</c> or
	/// <c>room.exits</c> push, or <see cref="Clear"/>. It fires after <see cref="ChannelUpdated"/>.
	/// </summary>
	event Action? RoomChanged;

	void Set(string package, string dataJson);
	string? Get(string package);
	IReadOnlyCollection<string> Packages { get; }

	/// <summary>
	/// The latest room payloads, typed (<see cref="OobEntryParser"/>). Each push replaces its part
	/// whole; a snapshot read once stays consistent however many pushes follow.
	/// </summary>
	RoomState Room { get; }

	/// <summary>
	/// Drops all cached payloads (e.g. on a new connection/login) so a fresh session never renders
	/// stale data from a previous one. Raises <see cref="ChannelUpdated"/> for each cleared package
	/// so subscribers re-read and reset, and <see cref="RoomChanged"/> once.
	/// </summary>
	void Clear();
}
