namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A stored role category, keyed by its lowercased name in <c>role.cat</c>.</summary>
public sealed record RoleCategoryRecord
{
	public string Name { get; init; } = "";
	public string Description { get; init; } = "";
	public long CreatedAt { get; init; }
}
