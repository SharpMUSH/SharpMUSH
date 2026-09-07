namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.ChannelDbRecord</c>. <c>Name</c> is the plain-text name used for lookup;
/// <c>MarkedUpName</c> and <c>Description</c> are <c>MModule.serialize</c> output.
/// </summary>
public sealed record ChannelRecord
{
	public string Name { get; init; } = "";
	public string MarkedUpName { get; init; } = "";
	public string Description { get; init; } = "";
	public string[] Privs { get; init; } = [];
	public string JoinLock { get; init; } = "";
	public string SpeakLock { get; init; } = "";
	public string SeeLock { get; init; } = "";
	public string HideLock { get; init; } = "";
	public string ModLock { get; init; } = "";
	public string Mogrifier { get; init; } = "";
	public int Buffer { get; init; }
}

/// <summary>
/// Mirrors <c>SurrealDatabase.ChannelMemberEdgeRecord</c>; the member's dbref is part of the LMDB key,
/// not a field here. <c>Title</c> is <c>MModule.serialize</c> output.
/// </summary>
public sealed record ChannelMemberRecord
{
	public bool Gagged { get; init; }
	public bool Mute { get; init; }
	public bool Hide { get; init; }
	public bool Combine { get; init; }
	public string Title { get; init; } = "";
}
