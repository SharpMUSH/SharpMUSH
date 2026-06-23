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
}
