namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// The dictionary a kind's new values are compressed with, in <see cref="Store.Tables.Meta"/> beside the dictionaries
/// themselves: its id, the values of the kind the world held when it was trained, and whether every value has been
/// rewritten with it.
/// </summary>
public sealed record DictionaryStateRecord
{
	public int Id { get; init; }
	public long Rows { get; init; }
	public bool Complete { get; init; }
}

/// <summary>One training run, kept or not, under <c>&lt;kind&gt;.dictrun:</c> and its number.</summary>
public sealed record DictionaryRunRecord
{
	public long AtMs { get; init; }
	public long Rows { get; init; }
	public int SampleRows { get; init; }
	public int HeldOutRows { get; init; }
	public long CurrentBytes { get; init; }
	public long CandidateBytes { get; init; }
	public int From { get; init; }
	public int? Kept { get; init; }
}
