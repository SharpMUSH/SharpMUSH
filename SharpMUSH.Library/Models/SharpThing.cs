using Newtonsoft.Json;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpThing
{

	[JsonIgnore]
	public string? Id { get; set; }

	public string[]? Aliases { get; set; }

	// Relationship
	[JsonIgnore]
	public required SharpObject Object { get; set; }

	// Relationship
	[JsonIgnore]
	public required Func<AnySharpContainer> Location { get; set; }

	// Relationship
	[JsonIgnore]
	public required Func<AnySharpContainer> Home { get; set; }
}