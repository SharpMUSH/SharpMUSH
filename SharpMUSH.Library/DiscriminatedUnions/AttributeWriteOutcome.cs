namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// What a store write to an attribute did: it wrote nothing watched, it changed one or more watched attributes (and
/// says how), or there was nothing to write to. An attribute is watched when its entry has an
/// <c>@attribute/watch</c> pattern.
/// </summary>
/// <remarks>
/// The caller names the watched attributes (<see cref="Models.WatchedAttributes"/>), and the store reads a previous
/// value inside the write's own transaction only for those, so a write to an unwatched attribute costs nothing more.
/// </remarks>
public union AttributeWriteOutcome(Success, WatchedAttributesChanged, NotFound);

/// <summary>The write changed watched attributes, each listed once, in the order written.</summary>
public readonly record struct WatchedAttributesChanged(IReadOnlyList<WatchedAttributeChange> Changes);

/// <summary>One watched attribute's change.</summary>
/// <param name="Name">The attribute's full name, upper case (<c>HP</c>, <c>STATS`HP</c>).</param>
/// <param name="OldValue">Its value before the write; empty when the write created it.</param>
/// <param name="NewValue">Its value after the write; empty when the write cleared or removed it.</param>
public readonly record struct WatchedAttributeChange(string Name, MString OldValue, MString NewValue);

/// <summary>Answers for an <see cref="AttributeWriteOutcome"/>.</summary>
public static class AttributeWriteOutcomeExtensions
{
	/// <summary>Whether the write happened, watched or not.</summary>
	public static bool Written(this AttributeWriteOutcome outcome) => outcome is not NotFound;
}
