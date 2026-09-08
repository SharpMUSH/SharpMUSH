namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.RoleDbRecord</c>. <c>Permissions</c> is stored as a native map — the
/// per-scope <c>PermissionState</c> cast to <c>int</c> — rather than SurrealDB's nested JSON string,
/// since the JSON codec already gives every value its own document.
/// </summary>
public sealed record RoleRecord
{
	public string Slug { get; init; } = "";
	public string Name { get; init; } = "";
	public string? Color { get; init; }
	public int Priority { get; init; }
	public bool IsSystem { get; init; }
	public Dictionary<string, int> Permissions { get; init; } = new();
	public long CreatedAt { get; init; }
	public long UpdatedAt { get; init; }
}
