using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.API;

public class ConfigurationResponse
{
	/// <summary>
	/// Current configuration values
	/// </summary>
	[JsonPropertyName("configuration")]
	public SharpMUSHOptions Configuration { get; set; } = null!;

	/// <summary>
	/// Basic metadata (legacy format)
	/// </summary>
	[JsonPropertyName("metadata")]
	public Dictionary<string, SharpConfigAttribute> Metadata { get; set; } = [];

	/// <summary>
	/// Enhanced metadata for rich UI rendering
	/// </summary>
	[JsonPropertyName("schema")]
	public ConfigurationSchema Schema { get; set; } = new();
}

/// <summary>
/// Complete schema for configuration UI
/// </summary>
public class ConfigurationSchema
{
	/// <summary>
	/// All categories (sections)
	/// </summary>
	[JsonPropertyName("categories")]
	public List<CategoryMetadata> Categories { get; set; } = new();

	/// <summary>
	/// All properties with enhanced metadata
	/// </summary>
	[JsonPropertyName("properties")]
	public Dictionary<string, PropertyMetadata> Properties { get; set; } = new();
}