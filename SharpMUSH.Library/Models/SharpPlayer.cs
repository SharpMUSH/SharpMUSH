using Newtonsoft.Json;
using OneOf;

namespace SharpMUSH.Library.Models
{
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
		public required Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Location { get; set; }

		// Relationship
		[JsonIgnore]
		public required Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Home { get; set; }

		public required string PasswordHash { get; set; }
	}
}
