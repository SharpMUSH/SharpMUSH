using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpAttribute
{
	public required string Name { get; set; }

	public required Func<IEnumerable<SharpAttributeFlag>> Flags { get; set; }

	public string Value { get; set; } = string.Empty;

	// Computed Value
	[JsonIgnore]
	public virtual string? LongName { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public Func<IEnumerable<SharpAttribute>> Leaves { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public Func<SharpPlayer> Owner { get; set; }

	// RELATIONSHIP for quick lookups
	[JsonIgnore]
	public Func<SharpAttributeEntry?> SharpAttributeEntry { get; set; }
}