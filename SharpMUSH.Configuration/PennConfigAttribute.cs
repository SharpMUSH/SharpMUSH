namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class PennConfigAttribute : Attribute
{
	public required string Name { get; set; }
	public required string Description { get; set; }
	public string? Category { get; set; }
	public bool Nullable { get; set; } = false;
	public string? DefaultValue { get; set; }
	public string? ValidationPattern { get; set; }
	public string? HelpText { get; set; }
}