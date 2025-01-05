using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpRoom
{

	[JsonIgnore]
	public string? Id { get; set; }
	
	public string[]? Aliases { get; set; }

	[JsonIgnore]
	public required SharpObject Object { get; set; }
}