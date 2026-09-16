using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Adds a name to the attribute table only if it is free, and answers null if it was not. Unlike
/// <see cref="CreateAttributeEntryCommand"/>, which <c>@attribute/access</c> needs for its
/// set-whether-or-not-it-exists semantics, this leaves an existing definition exactly as it is.
/// </summary>
public record CreateAttributeEntryIfAbsentCommand(string Name, string[] DefaultFlags, string? Limit = null, string[]? EnumValues = null)
	: ICommand<SharpAttributeEntry?>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}
