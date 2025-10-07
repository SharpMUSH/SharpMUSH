namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class SharpConfigAttribute : Attribute
{
	public required string Name { get; set; }
	public required string Description { get; set; }
	public required string Category { get; set; }
	public string? ValidationPattern { get; set; }
}