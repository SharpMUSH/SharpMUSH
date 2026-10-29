namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// The value under a number in <c>Tables.Recover</c>: a destroyed object's row, moved out of <c>Tables.Obj</c>
/// unchanged, and what it pointed at as objids (<c>#N:ms</c>). Its attributes and expanded data stay under the
/// number in their own tables.
/// </summary>
public sealed record RecoverRecord
{
	public ObjectRecord Object { get; init; } = new();
	public string? Owner { get; init; }
	public string? Location { get; init; }
	public string? Home { get; init; }
	public string? Parent { get; init; }
	public string? Zone { get; init; }
	public string[] Flags { get; init; } = [];
	public string[] Powers { get; init; } = [];
	public string[] Roles { get; init; } = [];
	/// <summary>The object's permission overrides, as <c>Tables.ObjPermission</c> held them.</summary>
	public byte[]? Permissions { get; init; }
	/// <summary>The key of the account a character was linked to.</summary>
	public string? Account { get; init; }
	public string? DestroyedBy { get; init; }
	public long DestroyedAt { get; init; }
	public string? KeptBy { get; init; }
	public string? KeepReason { get; init; }
	public long? KeptAt { get; init; }
	/// <summary>Bytes its attribute and expanded-data rows occupy, keys and values, counted when it was retired.</summary>
	public long Bytes { get; init; }
}
