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
}
