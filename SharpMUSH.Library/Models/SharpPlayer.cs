using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

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

	public string? PasswordSalt { get; set; }

	/// <summary>
	/// The player's build quota - maximum number of objects they can own.
	/// </summary>
	public required int Quota { get; set; }
}
