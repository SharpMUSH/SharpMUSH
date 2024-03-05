﻿using Core.Arango;
using Newtonsoft.Json;

namespace SharpMUSH.Database.Types
{
	public class SharpEdge
	{
		[ArangoIgnore]
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
}
