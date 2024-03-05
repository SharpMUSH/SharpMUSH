using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpExit
	{
		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Object { get; set; }

		public string[]? Aliases { get; set; }

		// Relationship
		[JsonIgnore]
		public required SharpObject Location { get; set; } // DESTINATION

		// Relationship
		[JsonIgnore]
		public required SharpObject Home { get; set; } // SOURCE ROOM
	}
}
