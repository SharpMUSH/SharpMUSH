namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// A stored mail alias, keyed <c>Keys.Upper(Name)</c>. <c>Sequence</c> is its place in creation order,
/// which is the order PennMUSH lists aliases in; the key alone would list them alphabetically.
/// </summary>
public sealed record MailAliasRecord
{
	public long Sequence { get; init; }
	public string Name { get; init; } = "";
	public string Description { get; init; } = "";
	public int Owner { get; init; }
	public int[] Members { get; init; } = [];
	public int UsePrivileges { get; init; }
	public int SeePrivileges { get; init; }
}
