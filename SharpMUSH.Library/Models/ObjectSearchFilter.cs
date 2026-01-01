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
	/// Filter by flag name. Object must have this flag. Null means no flag filtering.
	/// </summary>
	public string? HasFlag { get; init; }

	/// <summary>
	/// Filter by power name. Object must have this power. Null means no power filtering.
	/// </summary>
	public string? HasPower { get; init; }

	/// <summary>
	/// Empty filter that matches all objects
	/// </summary>
	public static ObjectSearchFilter Empty => new();

	/// <summary>
	/// Check if this filter has any active filters
	/// </summary>
	public bool HasFilters => Types != null || Owner != null || NamePattern != null || 
	                          MinDbRef != null || MaxDbRef != null || Zone != null || 
	                          Parent != null || HasFlag != null || HasPower != null;
}
