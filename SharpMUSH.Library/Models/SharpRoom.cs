using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpRoom
{
	[JsonIgnore]
	public string? Id { get; set; }

	public string[]? Aliases { get; set; }

	[JsonIgnore]
	public required SharpObject Object { get; set; }

	// Relationship - Location (Drop-To for rooms)
	[JsonIgnore]
	public required AsyncLazy<AnyOptionalSharpContainer> Location { get; set; }
}