using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models;

public class SharpAttribute
{
	public required string Name { get; set; }

	public required Func<IEnumerable<SharpAttributeFlag>> Flags { get; set; }

	public MString Value { get; set; } = MModule.empty();

	public int? CommandListIndex { get; set; }

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