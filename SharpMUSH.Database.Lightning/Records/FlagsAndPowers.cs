namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Mirrors <c>SharpMUSH.Library.Models.SharpObjectFlag</c>.</summary>
public sealed record FlagRecord
{
	public string Name { get; init; } = "";
	public string Symbol { get; init; } = "";
	public string[] Aliases { get; init; } = [];
	public string[] SetPermissions { get; init; } = [];
	public string[] UnsetPermissions { get; init; } = [];
	public string[] TypeRestrictions { get; init; } = [];
	public bool System { get; init; }
	public bool Disabled { get; init; }
}

/// <summary>Mirrors <c>SharpMUSH.Library.Models.SharpPower</c>.</summary>
public sealed record PowerRecord
{
	public string Name { get; init; } = "";
	public string Alias { get; init; } = "";
	public string Symbol { get; init; } = "";
	public string[] SetPermissions { get; init; } = [];
	public string[] UnsetPermissions { get; init; } = [];
	public string[] TypeRestrictions { get; init; } = [];
	public bool System { get; init; }
	public bool Disabled { get; init; }
}

/// <summary>Mirrors <c>SharpMUSH.Library.Models.SharpAttributeFlag</c>.</summary>
public sealed record AttributeFlagRecord
{
	public string Name { get; init; } = "";
	public string Symbol { get; init; } = "";
	public bool Inheritable { get; init; }
	public bool System { get; init; }
}

/// <summary>Mirrors <c>SharpMUSH.Library.Models.SharpAttributeEntry</c>.</summary>
public sealed record AttributeEntryRecord
{
	public string Name { get; init; } = "";
	public string[] DefaultFlags { get; init; } = [];
	public string? Limit { get; init; }
	public string[]? Enum { get; init; }
}
