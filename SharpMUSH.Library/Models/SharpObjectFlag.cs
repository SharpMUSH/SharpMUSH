using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpObjectFlag
{
	[JsonIgnore]
	public string? Id { get; init; }

	/// <summary>
	/// The identifiable name.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Aliases by which the flag can be identified.
	/// </summary>
	public string[]? Aliases { get; init; }

	/// <summary>
	/// The single character symbol used to represent the flag.
	/// </summary>
	/// <remarks>
	/// Not unique.
	/// </remarks>
	public required string Symbol { get; init; }

	public required string[] SetPermissions { get; init; }

	public required string[] UnsetPermissions { get; init; }

	/// <summary>
	/// Indicates this is an internally used flag and cannot be deleted.
	/// </summary>
	public required bool System { get; init; }

	/// <summary>
	/// Indicates if this flag is currently disabled and cannot be set on objects.
	/// System flags cannot be disabled.
	/// </summary>
	public bool Disabled { get; init; } = false;

	/// <summary>
	/// What internal type(s) this object flag can be set to.
	/// </summary>
	public required string[] TypeRestrictions { get; init; }
}