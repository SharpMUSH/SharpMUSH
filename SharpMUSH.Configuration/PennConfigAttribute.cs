namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class PennConfigAttribute : Attribute
{
	public required string Name { get; set; }
}