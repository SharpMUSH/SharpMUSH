using Core.Arango;
using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpAttribute
	{
		public required string Name { get; set; }

		public required string[] Flags { get; set; }

		// Computed Value
		[ArangoIgnore]
		public virtual string? LongName { get; set; }

		[JsonIgnore]
		public virtual SharpAttribute[]? Leaves { get; set; }

		// RELATIONSHIP for quick lookups
		[JsonIgnore]
		public virtual SharpAttributeEntry? SharpAttributeEntry { get; set; }
	}
}