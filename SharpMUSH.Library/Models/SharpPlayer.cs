﻿using Newtonsoft.Json;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpPlayer
{
	[JsonIgnore]
	public string? Id { get; set; }

	// Relationship
	[JsonIgnore]
	public required SharpObject Object { get; set; }

	public string[]? Aliases { get; set; }

	// Relationship
	[JsonIgnore]
	public required Func<AnySharpContainer> Location { get; set; }

	// Relationship
	[JsonIgnore]
	public required Func<AnySharpContainer> Home { get; set; }

	public required string PasswordHash { get; set; }
}