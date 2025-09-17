using System.Text.Json.Serialization;
using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpPlayer
{
	[JsonIgnore]
	public string? Id { get; set; }

	// Relationship
	[JsonIgnore]
	public required SharpObject Object { get; set; }

	public string[]? Aliases { get; set; }

	// Relationship
	[JsonIgnore]
	public required AsyncLazy<AnySharpContainer> Location { get; set; }

	// Relationship
	[JsonIgnore]
	public required AsyncLazy<AnySharpContainer> Home { get; set; }

	public required string PasswordHash { get; set; }
}