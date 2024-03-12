using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpRoom
	{

		[JsonIgnore]
		public string? Id { get; set; }

		// Relationship
		[JsonIgnore]
		public SharpObject? Object { get; set; }
	}
}
