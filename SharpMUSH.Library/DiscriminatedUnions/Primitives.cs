// Local replacements for OneOf.Types primitives.
// API surface is intentionally identical to OneOf.Types so that
// call sites only need type-name changes, not structural changes.

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>Represents the absence of a value. Drop-in replacement for OneOf.Types.None.</summary>
public readonly record struct None;

/// <summary>Represents a string-valued error. Replaces Error&lt;string&gt; from OneOf.Types.</summary>
public readonly record struct SharpError(string Value);

/// <summary>Represents a CallState-valued error. Replaces Error&lt;CallState&gt; from OneOf.Types.</summary>
public readonly record struct SharpErrorCallState(ParserInterfaces.CallState Value);

/// <summary>Represents a string[]-valued error. Replaces Error&lt;string[]&gt; from OneOf.Types.</summary>
public readonly record struct SharpErrorList(string[] Values);

/// <summary>Represents success with no value. Replaces OneOf.Types.Success.</summary>
public readonly record struct SharpSuccess;
