﻿using Newtonsoft.Json;

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
		public virtual int Key { get; set; }

		public DBRef DBRef => new(Key!,CreationTime);

		public required string Name { get; set; }

		public required string Type { get; set; }

		public required Dictionary<string, SharpLock> Locks { get; set; }

		public long CreationTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
		
		public long ModifiedTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

		// RELATIONSHIP
		[JsonIgnore]
		public required IQueryable<SharpPlayer> Owner { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public required IQueryable<SharpPower> Powers { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public required IQueryable<SharpAttribute> Attributes { get; set; }

		// RELATIONSHIP
		[JsonIgnore]
		public required IQueryable<SharpObjectFlag> Flags { get; set; }
		
		// RELATIONSHIP
		[JsonIgnore]
		public required IQueryable<SharpObject> Parent { get ; set; }
	}
}