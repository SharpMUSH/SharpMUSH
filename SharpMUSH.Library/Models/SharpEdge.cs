using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpEdge
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonProperty("_from")]
	public required string From { get; set; }

	[JsonProperty("_to")]
	public required string To { get; set; }
}

public class SharpHookEdge : SharpEdge
{
	public required string Type { get; set; }
}