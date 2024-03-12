using Newtonsoft.Json;

namespace SharpMUSH.Library.Models
{
	public class SharpObject
	{
		public class SharpLock
		{
			public required string Value { get; set; }

			// TODO: This should be specific attributes. OSUCCESS, ASUCCESS, OFAIL, AFAIL, etc.
			public required string[] AttributeTriggers { get; set; }
		}

		[JsonIgnore]
		public string? Id { get; set; }

		[JsonProperty("_key")]
		public virtual int? Key { get; set; }

		public required string Name { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpPlayer? Owner { get; set; }

		public Dictionary<string, SharpLock>? Locks { get; set; }

		public long CreationTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

		public string[]? Powers { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpAttribute[]? Attributes { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpObjectFlag[]? Flags { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public virtual SharpObject? Parent { get; set; }
	}
}
