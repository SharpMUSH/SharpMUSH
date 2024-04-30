using Newtonsoft.Json;
using OneOf;

namespace SharpMUSH.Library.Models
{
	public class SharpExit
	{
		[JsonIgnore]
		public string? Id { get; set; }

		public string[]? Aliases { get; set; }

		public required SharpObject Object { get; set; }

		// Relationship
		[JsonIgnore]
		public required Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Location { get; set; } // DESTINATION

		// Relationship
		[JsonIgnore]
		public required Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Home { get; set; } // SOURCE ROOM
	}
}
