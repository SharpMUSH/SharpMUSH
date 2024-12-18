using Newtonsoft.Json;
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
	public required Lazy<AnySharpContainer> Location { get; set; }

	// Relationship
	[JsonIgnore]
	public required Lazy<AnySharpContainer> Home { get; set; }

	public required string PasswordHash { get; set; }
}