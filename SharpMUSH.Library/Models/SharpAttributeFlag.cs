using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpAttributeFlag
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonIgnore]
	public string? Key { get; set; }

	/// <summary>
	/// The identifiable name.
	/// </summary>
	public required string Name { get; set; }

	/// <summary>
	/// The single character symbol used to represent the flag.
	/// </summary>
	/// <remarks>
	/// Not unique.
	/// </remarks>
	public required string Symbol { get; set; }

	/// <summary>
	/// Indicates this is an internally used flag and cannot be deleted.
	/// </summary>
	public required bool System { get; set; }
}