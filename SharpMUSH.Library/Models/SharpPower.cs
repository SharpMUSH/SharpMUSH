using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpPower
{
	[JsonIgnore]
	public string? Id { get; set; }

	public required string Name { get; set; }

	public required bool System { get; set; }

	/// <summary>
	/// Indicates if this power is currently disabled and cannot be set on objects.
	/// System powers cannot be disabled.
	/// </summary>
	public bool Disabled { get; set; } = false;

	public required string Alias { get; set; }

	/// <summary>
	/// The power's one-character abbreviation, or the empty string for none — PennMUSH's
	/// <c>FLAG.letter</c>, set with <c>@power/letter</c>. Two powers that can apply to the same
	/// object type may not share one; see PennMUSH src/flags.c letter_to_flagptr.
	/// </summary>
	public string Symbol { get; set; } = string.Empty;

	public required string[] SetPermissions { get; set; }

	public required string[] UnsetPermissions { get; set; }

	public required string[] TypeRestrictions { get; set; }
}