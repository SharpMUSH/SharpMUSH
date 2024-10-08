﻿using Newtonsoft.Json;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Models;

public class SharpObject
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonProperty("_key")]
	public int Key { get; set; }

	public DBRef DBRef => new(Key, CreationTime);

	public required string Name { get; set; }

	public required string Type { get; set; }

	public required IImmutableDictionary<string, string> Locks { get; set; }

	public long CreationTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	public long ModifiedTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	// RELATIONSHIP
	[JsonIgnore]
	public required Func<SharpPlayer> Owner { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Func<IEnumerable<SharpPower>> Powers { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Func<IEnumerable<SharpAttribute>> Attributes { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Func<IEnumerable<SharpObjectFlag>> Flags { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Func<SharpObject?> Parent { get; set; }
}