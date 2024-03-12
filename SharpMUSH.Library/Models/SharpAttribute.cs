using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpAttribute
	{
		[JsonIgnore]
		public string? Id { get; set; }

		public required string Name { get; set; }

		public required string[] Flags { get; set; }

		public string Value { get; set; } = string.Empty;

		// Computed Value
		[JsonIgnore]
		public virtual string? LongName { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpAttribute[]? Leaves { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpPlayer? Owner { get; set; }

		// RELATIONSHIP for quick lookups
		[JsonIgnore]
		public virtual SharpAttributeEntry? SharpAttributeEntry { get; set; }
	}
}