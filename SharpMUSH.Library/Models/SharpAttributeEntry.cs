using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpAttributeEntry
	{
		[JsonIgnore]
		public string? Id { get; set; }

		public required string Name { get; set; }

		public required string[] DefaultFlags { get; set; }

		public string? Limit { get; set; }
		
		public string[]? Enum { get; set; }
	}
}