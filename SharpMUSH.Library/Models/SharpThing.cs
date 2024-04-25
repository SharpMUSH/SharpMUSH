using Newtonsoft.Json;
using OneOf;

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
		public Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Location { get; set; }

		// Relationship
		[JsonIgnore]
		public Func<OneOf<SharpPlayer, SharpRoom, SharpThing>> Home { get; set; }
	}
}
