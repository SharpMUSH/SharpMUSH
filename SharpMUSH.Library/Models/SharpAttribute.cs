﻿using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpAttribute
{
	public required string Key { get; set; }

	public required string Name { get; set; }

	public required IEnumerable<SharpAttributeFlag> Flags { get; set; }

	public MString Value { get; set; } = MModule.empty();

	public int? CommandListIndex { get; set; }

	// Computed Value
	[JsonIgnore]
	public virtual string? LongName { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Lazy<IEnumerable<SharpAttribute>> Leaves { get; set; }

	// RELATIONSHIP
	[JsonIgnore]
	public required Lazy<SharpPlayer> Owner { get; set; }

	// RELATIONSHIP for quick lookups
	[JsonIgnore]
	public required Lazy<SharpAttributeEntry?> SharpAttributeEntry { get; set; }
}