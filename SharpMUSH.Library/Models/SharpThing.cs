using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpThing
	{

		[JsonIgnore]
		public string? Id { get; set; }

		public string[]? Aliases { get; set; }

		// Relationship
		[JsonIgnore]
		public required SharpObject Object { get; set; }

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Location { get; set; }

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Home { get; set; }
	}
}
