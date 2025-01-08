namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Parameter)]
public class PennConfigAttribute : Attribute
{
	public required string Name { get; set; }
}