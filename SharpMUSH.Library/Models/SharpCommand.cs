using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpCommand
{
	[JsonIgnore]
	public string? Id { get; set; }

	public required string Name { get; set; }

	public string? Alias { get; set; }
		
	public required bool Enabled { get; set; }

	public string? RestrictedErrorMessage { get; set; }

	// NoParse, EqSplit, LSArgs, RSArgs, RSNoParse
	public required string[] Traits { get; set; }

	public required string[] Restrictions { get; set; }

	// Relationship
	[JsonIgnore]
	public SharpCommand? ClonedFrom { get; set; }

	// Relationship would need a Type field.
	[JsonIgnore]
	public Dictionary<string,SharpAttribute>? Hooks { get; set; }
}