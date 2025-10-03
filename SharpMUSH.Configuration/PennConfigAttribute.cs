namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class PennConfigAttribute<T> : Attribute
{
	public required string Name { get; set; }
	public required string Description { get; set; }
	public string? Category { get; set; }
	public bool Nullable { get; set; } = false;
	public T? DefaultValue { get; set; }
	public string? ValidationPattern { get; set; }
	public string? HelpText { get; set; }
}

// Non-generic version for backward compatibility
[AttributeUsage(AttributeTargets.Property)]
public class PennConfigAttribute : PennConfigAttribute<string>
{
}