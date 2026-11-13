namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// The value stored under an attribute's <c>Tables.AttrMeta</c> row (keyed by <c>Keys.Attr</c>). The
/// attribute's value lives separately in <c>Tables.AttrVal</c>; this is only its metadata.
/// </summary>
public sealed record AttrMetaRecord
{
	public long? Owner { get; init; }
	public string[] Flags { get; init; } = [];
	public string? Entry { get; init; }

	/// <summary>
	/// When the attribute's value was last written, in Unix milliseconds (UTC, 64-bit). Null for a row written before
	/// attributes carried the time, and for a branch that has only ever held the empty value its children's writes gave it.
	/// </summary>
	public long? Modified { get; init; }
}
