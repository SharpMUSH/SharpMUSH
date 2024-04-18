using Newtonsoft.Json;

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
		public virtual SharpObject? Location { get; set; } // DESTINATION

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Home { get; set; } // SOURCE ROOM
	}
}
