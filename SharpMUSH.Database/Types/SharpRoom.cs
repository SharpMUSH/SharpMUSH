using Core.Arango;
using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpRoom
	{

		[ArangoIgnore]
		public string? Id { get; set; }

		// Relationship
		[JsonIgnore]
		public SharpObject? Object { get; set; }
	}
}
