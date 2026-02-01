namespace SharpMUSH.Client.Models;

public class ConfigSchema
{
	public List<ConfigCategory> Categories { get; set; } = new();
}

public class ConfigCategory
{
	public string Name { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string? Description { get; set; }
	public List<ConfigProperty> Properties { get; set; } = new();
}

public class ConfigProperty
{
	public string Name { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string Type { get; set; } = string.Empty; // "string", "integer", "boolean", etc.
	public object? DefaultValue { get; set; }
	public List<string>? EnumValues { get; set; }
	public int? MinValue { get; set; }
	public int? MaxValue { get; set; }
}
