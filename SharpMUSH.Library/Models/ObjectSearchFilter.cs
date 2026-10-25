namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents filters that can be applied at the database level for efficient object searching.
/// Lock evaluation must happen in application code, but other filters can be pushed to the database.
/// </summary>
public record ObjectSearchFilter
{
	/// <summary>
	/// Filter by object type(s). Null means no type filtering.
	/// Examples: "PLAYER", "ROOM", "EXIT", "THING"
	/// </summary>
	public string[]? Types { get; init; }

	/// <summary>
	/// Filter by owner DBRef. Null means no owner filtering.
	/// </summary>
	public DBRef? Owner { get; init; }

	/// <summary>
	/// Filter by name pattern (case-insensitive substring match). Null means no name filtering.
	/// </summary>
	public string? NamePattern { get; init; }

	/// <summary>
	/// Whether to treat NamePattern as a regex pattern. Default is false (wildcard/substring match).
	/// </summary>
	public bool UseRegex { get; init; }

	/// <summary>
	/// Filter by minimum DBRef number. Null means no minimum.
	/// </summary>
	public int? MinDbRef { get; init; }

	/// <summary>
	/// Filter by maximum DBRef number. Null means no maximum.
	/// </summary>
	public int? MaxDbRef { get; init; }

	/// <summary>
	/// Filter by zone DBRef. Null means no zone filtering.
	/// </summary>
	public DBRef? Zone { get; init; }

	/// <summary>
	/// Filter by parent DBRef. Null means no parent filtering.
	/// </summary>
	public DBRef? Parent { get; init; }

	/// <summary>
	/// Only objects with no parent: PennMUSH's <c>PARENT</c> class with an empty restriction.
	/// </summary>
	public bool NoParent { get; init; }

	/// <summary>
	/// Filter by flag name. Object must have this flag. Null means no flag filtering.
	/// </summary>
	public string? HasFlag { get; init; }

	/// <summary>
	/// Filter by power name. Object must have this power. Null means no power filtering.
	/// </summary>
	public string? HasPower { get; init; }

	/// <summary>
	/// Filter by an indexed attribute's value (<c>lsearch</c>'s <c>INDEX</c> class): the object's own copy of the
	/// attribute is listed in its index under a value equal to the probe, as <c>indexed(&lt;attr&gt;, =, &lt;value&gt;)</c>
	/// finds it. The candidates come from the index. Null means no attribute filtering; an attribute with no ready index
	/// matches nothing.
	/// </summary>
	public AttributeIndexProbe? AttributeValue { get; init; }

	/// <summary>
	/// Number of results to skip (for pagination with START). Null means no skip.
	/// </summary>
	public int? Skip { get; init; }

	/// <summary>
	/// Maximum number of results to return (for pagination with COUNT). Null means no limit.
	/// </summary>
	public int? Limit { get; init; }

	/// <summary>
	/// Empty filter that matches all objects
	/// </summary>
	public static ObjectSearchFilter Empty => new();

	/// <summary>
	/// Check if this filter has any active filters
	/// </summary>
	public bool HasFilters => Types != null || Owner != null || NamePattern != null ||
														MinDbRef != null || MaxDbRef != null || Zone != null ||
														Parent != null || NoParent || HasFlag != null || HasPower != null || AttributeValue != null ||
														Skip != null || Limit != null;
}

/// <summary>An attribute index probe: objects whose own copy of <paramref name="Name"/> the index lists under <paramref name="Value"/>.</summary>
/// <param name="Name">The attribute's long name.</param>
/// <param name="Value">The value, as typed; the index folds it.</param>
public sealed record AttributeIndexProbe(string Name, string Value);
