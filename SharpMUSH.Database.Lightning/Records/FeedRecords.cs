using System.Text.Json;
using System.Text.Json.Serialization;

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
	public long StoredBytes { get; init; }
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

/// <summary>
/// One line on a feed, keyed kind + 0x00 + key + 0x00 + id. A channel keeps 100 lines per channel and a busy game
/// many feeds, so the record is written short: one- and two-letter names, nothing written for a field that is empty
/// or repeats another, and the markup fields (<see cref="Text"/>, <see cref="LineBefore"/>, <see cref="LineAfter"/>,
/// <see cref="Line"/>) as the serialized markup's own JSON rather than a string holding it.
/// </summary>
public sealed record FeedMessageRecord
{
	[JsonPropertyName("i")] public long Id { get; init; }
	[JsonPropertyName("a")] public long AtMs { get; init; }
	[JsonPropertyName("s")] public string Speaker { get; init; } = "";
	[JsonPropertyName("sn")] public string? SpeakerName { get; init; }

	/// <summary>The executor's objid, or null when it is the speaker.</summary>
	[JsonPropertyName("e")] public string? Executor { get; init; }

	/// <summary>The executor's name, or null when it is the speaker's.</summary>
	[JsonPropertyName("en")] public string? ExecutorName { get; init; }

	[JsonPropertyName("l")] public string? Location { get; init; }
	[JsonPropertyName("ln")] public string? LocationName { get; init; }
	[JsonPropertyName("y")] public string? Style { get; init; }

	/// <summary>The message, serialized markup.</summary>
	[JsonPropertyName("t"), JsonConverter(typeof(RawJsonConverter))]
	public string Text { get; init; } = "";

	[JsonPropertyName("d")] public string? DisplayName { get; init; }

	/// <summary>
	/// The whole line as delivered is <see cref="LineBefore"/> + <see cref="Text"/> + <see cref="LineAfter"/> when
	/// either is set: the message is written once, and only what surrounds it twice.
	/// </summary>
	[JsonPropertyName("lb"), JsonConverter(typeof(RawJsonConverter))]
	public string? LineBefore { get; init; }

	/// <inheritdoc cref="LineBefore"/>
	[JsonPropertyName("la"), JsonConverter(typeof(RawJsonConverter))]
	public string? LineAfter { get; init; }

	/// <summary>The whole line as delivered, serialized markup, when it does not hold <see cref="Text"/> as one piece.</summary>
	[JsonPropertyName("lf"), JsonConverter(typeof(RawJsonConverter))]
	public string? Line { get; init; }

	[JsonPropertyName("au")] public string? Audience { get; init; }
	[JsonPropertyName("x")] public FeedIdentityRecord? ExternalIdentity { get; init; }

	/// <summary>The picture shown with the line, an objid or an image URL, or null.</summary>
	[JsonPropertyName("p")] public string? Picture { get; init; }

	/// <summary>What the line counts against the feed's byte limit.</summary>
	[JsonPropertyName("b")] public long Bytes { get; init; }
}

/// <summary>
/// The feed line dictionary new lines are compressed with, in <see cref="Store.Tables.Meta"/> beside the dictionaries
/// themselves: its id, the lines the world held when it was trained, and whether every line has been rewritten with it.
/// </summary>
public sealed record FeedDictionaryRecord
{
	public int Id { get; init; }
	public long Lines { get; init; }
	public bool Complete { get; init; }
}

/// <summary>One training run, kept or not, under <c>feed.dictrun:</c> and its number.</summary>
public sealed record FeedDictionaryRunRecord
{
	public long AtMs { get; init; }
	public long Lines { get; init; }
	public int SampleLines { get; init; }
	public int HeldOutLines { get; init; }
	public long CurrentBytes { get; init; }
	public long CandidateBytes { get; init; }
	public int From { get; init; }
	public int? Kept { get; init; }
}

/// <summary>Who spoke a line from outside the game (<see cref="SharpMUSH.Library.Models.ExternalFeedIdentity"/>).</summary>
public sealed record FeedIdentityRecord
{
	[JsonPropertyName("v")] public string Provider { get; init; } = "";
	[JsonPropertyName("u")] public string Subject { get; init; } = "";
	[JsonPropertyName("n")] public string Name { get; init; } = "";
	[JsonPropertyName("p")] public string? Picture { get; init; }
}

/// <summary>
/// A string property that holds JSON (serialized markup), written as that JSON and read back as its text, so the
/// document is not escaped into a string inside the record.
/// </summary>
public sealed class RawJsonConverter : JsonConverter<string>
{
	public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		return document.RootElement.GetRawText();
	}

	public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
		=> writer.WriteRawValue(value);
}
