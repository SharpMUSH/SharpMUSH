using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Palette;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// An object's palette entries, its <c>PALETTE`&lt;NAME&gt;</c> attributes and its parents', each read
/// once. Cached and expired exactly as <see cref="GetCommandAttributesQuery"/> is: by key on any
/// attribute write to the object, and by <see cref="Definitions.CacheTags.InheritedAttributes"/> for a
/// write to a parent.
/// </summary>
public record GetPaletteEntriesQuery(AnySharpObject SharpObject) : IQuery<PaletteEntries>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Palette(SharpObject.Object().DBRef);
	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes];
}

/// <summary>What an object's palette attributes declare.</summary>
/// <param name="Entries">At most <see cref="PaletteEntry.MaxPerObject"/>, by order, then name.</param>
/// <param name="Invalid">The attributes that are not an entry, with why: <c>&lt;NAME&gt;: &lt;reason&gt;</c>.</param>
/// <param name="Dropped">Valid entries past the object's limit.</param>
public record PaletteEntries(IReadOnlyList<PaletteEntry> Entries, IReadOnlyList<string> Invalid, int Dropped)
{
	public static readonly PaletteEntries None = new([], [], 0);
}
