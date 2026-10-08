namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A feed's (or a kind's) settings; a null is unset. <see cref="MaxAgeMs"/> is milliseconds.</summary>
public sealed record FeedSettingsRecord
{
	public int? MaxMessages { get; init; }
	public long? MaxBytes { get; init; }
	public int? MaxLength { get; init; }
	public long? MaxAgeMs { get; init; }
	public bool? Logged { get; init; }
	public string? Style { get; init; }
}

/// <summary>A feed kind, keyed by its name.</summary>
public sealed record FeedKindRecord
{
	public string Name { get; init; } = "";
	public string Owner { get; init; } = "";
	public string Description { get; init; } = "";
	public FeedSettingsRecord Settings { get; init; } = new();
	public Dictionary<string, string> Locks { get; init; } = [];
}

/// <summary>A feed, keyed kind + 0x00 + key: its settings, locks and what it holds.</summary>
public sealed record FeedRecord
{
	public string Kind { get; init; } = "";
	public string Key { get; init; } = "";
	public FeedSettingsRecord Settings { get; init; } = new();
	public Dictionary<string, string> Locks { get; init; } = [];
	public int Messages { get; init; }
	public long Bytes { get; init; }
	public long LastId { get; init; }
}

/// <summary>
/// A member of a feed, keyed kind + 0x00 + key + 0x00 + dbref. It carries the member's creation time, so a
/// membership left by an earlier holder of a recycled dbref is not theirs.
/// </summary>
public sealed record FeedMemberRecord
{
	public long CreationTime { get; init; }
	public long JoinedAt { get; init; }
	public bool Gag { get; init; }
	public long LastSeen { get; init; }
}

/// <summary>One line on a feed, keyed kind + 0x00 + key + 0x00 + id. <see cref="Text"/> is serialized markup.</summary>
public sealed record FeedMessageRecord
{
	public long Id { get; init; }
	public long AtMs { get; init; }
	public string Speaker { get; init; } = "";
	public string SpeakerName { get; init; } = "";
	public string Executor { get; init; } = "";
	public string ExecutorName { get; init; } = "";
	public string? Location { get; init; }
	public string LocationName { get; init; } = "";
	public string Style { get; init; } = "";
	public string Text { get; init; } = "";
	public string DisplayName { get; init; } = "";

	/// <summary>What the line counts against the feed's byte limit.</summary>
	public long Bytes { get; init; }
}
