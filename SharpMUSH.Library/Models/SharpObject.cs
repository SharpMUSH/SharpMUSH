using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpObject
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonPropertyName("_key")]
	public int Key { get; set; }

	public DBRef DBRef => new(Key, CreationTime);

	public required string Name { get; set; }

	public required string Type { get; set; }

	public required IImmutableDictionary<string, string> Locks { get; set; }

	public long CreationTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	public long ModifiedTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	// RELATIONSHIP
	[JsonIgnore]
	public required AsyncLazy<SharpPlayer> Owner { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required AsyncLazy<IEnumerable<SharpPower>> Powers { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required AsyncLazy<IEnumerable<SharpAttribute>> Attributes { get; set; }

	[JsonIgnore]
	public required AsyncLazy<IEnumerable<SharpAttribute>> AllAttributes { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required AsyncLazy<IEnumerable<SharpObjectFlag>> Flags { get; set; }

	// RELATIONSHIP
	// TODO: Consider using AnySharpObject instead of SharpObject
	[JsonIgnore]
	public required AsyncLazy<AnyOptionalSharpObject> Parent { get; set; }
	
	// RELATIONSHIP
	// TODO: Consider using AnySharpObject instead of SharpObject
	[JsonIgnore]
	public required AsyncLazy<IEnumerable<SharpObject>> Children { get; set; }
}