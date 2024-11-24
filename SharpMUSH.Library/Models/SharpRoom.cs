using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpRoom
{

		[JsonIgnore]
		public string? Id { get; set; }

		[JsonIgnore]
		public required SharpObject Object { get; set; }
}