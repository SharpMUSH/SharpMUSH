using System.Text.Json.Serialization;

namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class SharpConfigAttribute : Attribute
{
	[JsonIgnore] public override object TypeId => base.TypeId;

	public required string Name { get; set; }
	public required string Description { get; set; }
	public required string Category { get; set; }
	public string? ValidationPattern { get; set; }
	
	/// <summary>
	/// UI group name within the category (e.g., "Connection Settings", "Connection Limits")
	/// If null, property appears in a default/ungrouped section
	/// </summary>
	public string? Group { get; set; }
	
	/// <summary>
	/// Sort order within the group (lower = earlier)
	/// </summary>
	public int Order { get; set; } = 0;
	
	/// <summary>
	/// Minimum value for numeric types
	/// </summary>
	public object? Min { get; set; }
	
	/// <summary>
	/// Maximum value for numeric types
	/// </summary>
	public object? Max { get; set; }
	
	/// <summary>
	/// Additional tooltip text (shown in addition to Description)
	/// </summary>
	public string? Tooltip { get; set; }
}