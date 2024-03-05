using Core.Arango;
using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpThing
	{
		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Object { get; set; }

		public string[]? Aliases { get; set; }

		// Relationship
		[JsonIgnore]
		public required SharpObject Location { get; set; }

		// Relationship
		[JsonIgnore]
		public required SharpObject Home { get; set; }
	}
}
