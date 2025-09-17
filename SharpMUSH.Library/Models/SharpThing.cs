using System.Text.Json.Serialization;
using DotNext.Threading;
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
	public required AsyncLazy<AnySharpContainer> Location { get; set; }

	// Relationship
	[JsonIgnore]
	public required AsyncLazy<AnySharpContainer> Home { get; set; }
}