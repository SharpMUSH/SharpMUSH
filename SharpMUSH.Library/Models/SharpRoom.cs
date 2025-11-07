using System.Text.Json.Serialization;
using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpRoom
{
	[JsonIgnore]
	public string? Id { get; set; }
	
	public string[]? Aliases { get; set; }

	[JsonIgnore]
	public required SharpObject Object { get; set; }

	// Relationship - Drop-To room
	[JsonIgnore]
	public required AsyncLazy<AnyOptionalSharpContainer> DropTo { get; set; }
}