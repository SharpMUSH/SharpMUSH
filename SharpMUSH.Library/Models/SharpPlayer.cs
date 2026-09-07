using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpPlayer : IObjectShaped<SharpPlayer>
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonIgnore]
	public required SharpObject Object { get; set; }

	public string[]? Aliases { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnySharpContainer> Location { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnySharpContainer> Home { get; set; }

	public required string PasswordHash { get; set; }

	public string? PasswordSalt { get; set; }

	/// <summary>
	/// The player's build quota - maximum number of objects they can own.
	/// </summary>
	public required int Quota { get; set; }

	public static DBRef? RefOf(SharpPlayer value) => value.Object.DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out SharpPlayer value)
	{
		value = node.IsPlayer ? node.AsPlayer : null!;
		return node.IsPlayer;
	}
}
