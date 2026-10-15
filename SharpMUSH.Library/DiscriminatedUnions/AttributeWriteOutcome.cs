namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// What a store write to an attribute did: it wrote nothing <c>watched</c>, it changed one or more
/// <c>watched</c> attributes (and says how), or there was nothing to write to.
/// </summary>
/// <remarks>
/// The store answers the <c>watched</c> question inside the write's own transaction, from the attribute
/// metadata the write reads anyway, so a write to an unwatched attribute costs nothing more. Only a
/// <c>watched</c> attribute's previous value is read.
/// </remarks>
public union AttributeWriteOutcome(Success, WatchedAttributesChanged, NotFound);

/// <summary>The write changed attributes flagged <c>watched</c>, each listed once, in the order written.</summary>
public readonly record struct WatchedAttributesChanged(IReadOnlyList<WatchedAttributeChange> Changes);

/// <summary>One <c>watched</c> attribute's change.</summary>
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
