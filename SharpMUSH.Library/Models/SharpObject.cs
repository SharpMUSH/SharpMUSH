using Newtonsoft.Json;
using System.Collections.Immutable;
using FSharpPlus.Control;

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
	public required Lazy<SharpPlayer> Owner { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Lazy<IEnumerable<SharpPower>> Powers { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Lazy<IEnumerable<SharpAttribute>> Attributes { get; set; }

	[JsonIgnore]
	public required Lazy<IEnumerable<SharpAttribute>> AllAttributes { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Lazy<IEnumerable<SharpObjectFlag>> Flags { get; set; }

	// RELATIONSHIP
	// TODO: Consider using AnySharpObject instead of SharpObject
	[JsonIgnore]
	public required Lazy<SharpObject?> Parent { get; set; }
}