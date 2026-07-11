using System.Text.Json.Serialization;

namespace SharpMUSH.Library.API;

/// <summary>
/// Enhanced property metadata for rich UI rendering
/// </summary>
public class PropertyMetadata
{
	/// <summary>
	/// Property name (e.g., "Port")
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Display name for UI (e.g., "Port Number")
	/// </summary>
	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	/// <summary>
	/// Detailed description/help text
	/// </summary>
	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	/// <summary>
	/// Category/section name (e.g., "NetOptions")
	/// </summary>
	[JsonPropertyName("category")]
	public string Category { get; set; } = string.Empty;

	/// <summary>
	/// UI Group within category (e.g., "Connection Settings", "Connection Limits")
	/// </summary>
	[JsonPropertyName("group")]
	public string? Group { get; set; }

	/// <summary>
	/// Sort order within group (lower = earlier)
	/// </summary>
	[JsonPropertyName("order")]
	public int Order { get; set; }

	/// <summary>
	/// Data type (boolean, integer, string, etc.)
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; set; } = "string";

	/// <summary>
	/// Suggested UI component (switch, numeric, text, select, slider)
	/// </summary>
	[JsonPropertyName("component")]
	public string Component { get; set; } = "text";

	/// <summary>
	/// Default value (for reset/documentation)
	/// </summary>
	[JsonPropertyName("defaultValue")]
	public object? DefaultValue { get; set; }

	/// <summary>
	/// Minimum value (for numeric types)
	/// </summary>
	[JsonPropertyName("min")]
	public object? Min { get; set; }

	/// <summary>
	/// Maximum value (for numeric types)
	/// </summary>
	[JsonPropertyName("max")]
	public object? Max { get; set; }

	/// <summary>
	/// Validation regex pattern
	/// </summary>
	[JsonPropertyName("pattern")]
	public string? Pattern { get; set; }

	/// <summary>
	/// Is this field required?
	/// </summary>
	[JsonPropertyName("required")]
	public bool Required { get; set; }

	/// <summary>
	/// Options for select/dropdown (key = value, value = display text)
	/// </summary>
	[JsonPropertyName("options")]
	public Dictionary<string, string>? Options { get; set; }

	/// <summary>
	/// Read-only field (display only, no editing)
	/// </summary>
	[JsonPropertyName("readOnly")]
	public bool ReadOnly { get; set; }

	/// <summary>
	/// Additional tooltip text
	/// </summary>
	[JsonPropertyName("tooltip")]
	public string? Tooltip { get; set; }

	/// <summary>
	/// Full property path (e.g., "NetOptions.Port")
	/// </summary>
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Category-level metadata for organizing configuration sections
/// </summary>
public class CategoryMetadata
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	public string? Description { get; set; }

	[JsonPropertyName("icon")]
	public string? Icon { get; set; }

	[JsonPropertyName("order")]
	public int Order { get; set; }

	[JsonPropertyName("groups")]
	public List<GroupMetadata> Groups { get; set; } = new();
}

/// <summary>
/// Group metadata for cards within a category
/// </summary>
public class GroupMetadata
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	public string? Description { get; set; }

	[JsonPropertyName("icon")]
	public string? Icon { get; set; }

	[JsonPropertyName("order")]
	public int Order { get; set; }
}
