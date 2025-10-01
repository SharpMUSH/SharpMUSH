namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class PennConfigAttribute : Attribute
{
	public required string Name { get; set; }
	public string? Description { get; set; }
	public string? Category { get; set; }
	public bool IsAdvanced { get; set; } = false;
	public string? DefaultValue { get; set; }
	public string? ValidationPattern { get; set; }
	public string? HelpText { get; set; }
}