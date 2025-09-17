using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpEdge
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonPropertyName("_from")]
	public required string From { get; set; }

	[JsonPropertyName("_to")]
	public required string To { get; set; }
}

public class SharpHookEdge : SharpEdge
{
	public required string Type { get; set; }
}