using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpThing
{

	[JsonIgnore]
	public string? Id { get; set; }

	public string[]? Aliases { get; set; }

	[JsonIgnore]
	public required SharpObject Object { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnySharpContainer> Location { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnySharpContainer> Home { get; set; }
}