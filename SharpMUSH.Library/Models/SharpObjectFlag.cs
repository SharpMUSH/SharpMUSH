using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpObjectFlag
{
	[JsonIgnore]
	public string? Id { get; set; }

	/// <summary>
	/// The identifiable name.
	/// </summary>
	public required string Name { get; set; }

	/// <summary>
	/// Aliases by which the flag can be identified.
	/// </summary>
	public string[]? Aliases { get; set; }

	/// <summary>
	/// The single character symbol used to represent the flag.
	/// </summary>
	/// <remarks>
	/// Not unique.
	/// </remarks>
	public required string Symbol { get; set; }

	public required string[] SetPermissions { get; set; }

	public required string[] UnsetPermissions { get; set; }

	/// <summary>
	/// Indicates this is an internally used flag and cannot be deleted.
	/// </summary>
	public required bool System { get; set; }

	/// <summary>
	/// What internal type(s) this object flag can be set to.
	/// </summary>
	public required string[] TypeRestrictions { get; set; }
}