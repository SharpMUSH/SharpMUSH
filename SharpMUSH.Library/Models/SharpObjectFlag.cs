using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpObjectFlag
	{

		[JsonIgnore]
		public string? Id { get; set; }

		public required string Name { get; set; }
		
		public string[]? Aliases { get; set; }
		
		public required string Symbol { get; set; }
		
		public required string[] SetPermissions { get; set; }
		
		public required string[] UnsetPermissions { get; set; }

		public required string[] TypeRestrictions { get; set; }
	}
}