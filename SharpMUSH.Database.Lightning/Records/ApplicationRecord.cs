namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.SysApplicationDbRecord</c>. <c>Kind</c> and <c>MinimumRole</c> are
/// <c>ApplicationKind</c>/<c>PortalRole</c> serialized via <c>ToString()</c>; <c>Zones</c> is
/// <c>ApplicationRegistryMapping.ZonesToString</c> output.
/// </summary>
public sealed record ApplicationRecord
{
	public string Slug { get; init; } = "";
	public string DisplayName { get; init; } = "";
	public string? Icon { get; init; }
	public string Kind { get; init; } = "";
	public string SchemaUrl { get; init; } = "";
	public string? DataUrl { get; init; }
	public string? SubmitRoute { get; init; }
	public string MinimumRole { get; init; } = "";
	public string? NavPlacement { get; init; }
	public string Zones { get; init; } = "";
	public int SortOrder { get; init; }
	public string? OwningPackage { get; init; }
	public string? RenderKind { get; init; }
	public string? ComponentAssemblyUrl { get; init; }
	public string? ComponentTypeName { get; init; }
}
