namespace SharpMUSH.Database.Types
{
	public class SharpAttributeEntry
	{
		public required string Name { get; set; }

		public required string[] DefaultFlags { get; set; }

		public string? Limit { get; set; }
		
		public string[]? Enum { get; set; }
	}
}