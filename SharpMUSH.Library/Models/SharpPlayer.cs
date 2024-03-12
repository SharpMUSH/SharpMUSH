using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpPlayer
	{
		[JsonIgnore]
		public string? Id { get; set; }

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Object { get; set; }

		public string[]? Aliases { get; set; }

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Location { get; set; }

		// Relationship
		[JsonIgnore]
		public virtual SharpObject? Home { get; set; }

		public required string PasswordHash { get; set; }
	}
}
