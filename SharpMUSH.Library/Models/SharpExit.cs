using System.Text.Json.Serialization;
using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpExit
{
	[JsonIgnore]
	public string? Id { get; set; }

	public string[]? Aliases { get; set; }

	public required SharpObject Object { get; set; }

	// Relationship
	[JsonIgnore]
	public required AsyncLazy<AnySharpContainer> Location { get; set; } // DESTINATION

	// Relationship
	[JsonIgnore]
	public required AsyncLazy<AnySharpContainer> Home { get; set; } // SOURCE ROOM
}