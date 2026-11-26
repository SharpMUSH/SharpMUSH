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

	/// <summary>
	/// Raised for every package received, after it is stored, with what the frame said about it: who sent it
	/// and whether it arrived as catch-up after a resume. <see cref="ChannelUpdated"/> fires first.
	/// </summary>
	event Action<OobFrame>? FrameReceived;

	/// <summary>Raised when the connection's session changes in a way that makes its packages stale.</summary>
	event Action<OobSessionSignal>? SessionSignaled;

	/// <summary>Stores a package received live from an unknown sender.</summary>
	void Set(string package, string dataJson);

	/// <summary>Stores a package and raises <see cref="FrameReceived"/> with it.</summary>
	void Receive(OobFrame frame);

	/// <summary>Raises <see cref="SessionSignaled"/>.</summary>
	void Signal(OobSessionSignal signal);

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

/// <summary>One out-of-band package as the connection received it.</summary>
/// <param name="Package">The package name.</param>
/// <param name="DataJson">The payload's raw JSON.</param>
/// <param name="From">The objid of the object that sent it, when the server said.</param>
/// <param name="CatchUp">Sent while the page was away and delivered when its session resumed.</param>
public readonly record struct OobFrame(string Package, string DataJson, string? From = null, bool CatchUp = false);

/// <summary>A change to the connection's session.</summary>
public enum OobSessionSignal
{
	/// <summary>A resume was refused: the connection logged in afresh.</summary>
	ResumeRefused,

	/// <summary>The session ended: the server ended it, or the page disconnected.</summary>
	Ended
}
