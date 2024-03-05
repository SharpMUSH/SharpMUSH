using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpRoom
	{
		// Relationship
		[JsonIgnore]
		public SharpObject? Object { get; set; }
	}
}
