namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A stored custom permission, keyed by its scope in <c>perm.def</c>.</summary>
public sealed record CustomPermissionRecord
{
	public string Scope { get; init; } = "";
	public string Description { get; init; } = "";
	public long CreatedAt { get; init; }
}
