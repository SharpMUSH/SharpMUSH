namespace SharpMUSH.Database.Types
{
	public class SharpObjectFlag
	{
		public required string Name { get; set; }
		
		public string[]? Aliases { get; set; }
		
		public required string Symbol { get; set; }
		
		public required string[] SetPermissions { get; set; }
		
		public required string[] UnsetPermissions { get; set; }

		public required string[] TypeRestrictions { get; set; }
	}
}