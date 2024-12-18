using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpFunction
{
	[JsonIgnore]
	public string? Id { get; set; }

	public required string Name { get; set; }

	public string? Alias { get; set; }

	public required bool Enabled { get; set; }

	public string? RestrictedErrorMessage { get; set; }

	// NoParse, EqSplit, LSArgs, RSArgs, RSNoParse
	public required string[] Traits { get; set; }

	public required int MinArgs { get; set; }

	public required int MaxArgs { get; set; }

	public required string[] Restrictions { get; set; }

	// Relationship
	[JsonIgnore]
	public SharpFunction? ClonedFrom { get; set; }

	// Relationship
	[JsonIgnore]
	public SharpAttribute? Attribute { get; set; }
}