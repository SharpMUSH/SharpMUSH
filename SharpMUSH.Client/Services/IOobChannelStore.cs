namespace SharpMUSH.Client.Services;

/// <summary>
/// A generic, per-connection cache of the latest payload for each OOB package/channel. Holds no
/// knowledge of room/character semantics — consumers (e.g. the Play sidebar) interpret the JSON.
/// </summary>
public interface IOobChannelStore
{
	event Action<string>? ChannelUpdated;
	void Set(string package, string dataJson);
	string? Get(string package);
	IReadOnlyCollection<string> Packages { get; }

	/// <summary>
	/// Drops all cached payloads (e.g. on a new connection/login) so a fresh session never renders
	/// stale data from a previous one. Raises <see cref="ChannelUpdated"/> for each cleared package
	/// so subscribers re-read and reset.
	/// </summary>
	void Clear();
}
