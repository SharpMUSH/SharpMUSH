namespace SharpMUSH.Library.API;

/// <summary>
/// Wire models for <c>api/objects</c>, the typed object/attribute API the portal's Softcode Editor
/// writes through.
///
/// The point of these existing at all is the value carrier: an attribute value travels as a JSON
/// string, which carries newlines natively. The terminal channel it replaces is line-delimited,
/// which is why the editor previously had to rewrite newlines as <c>%r</c> before sending
/// <c>&amp;ATTR #dbref=value</c>.
/// </summary>
/// <param name="Dbref">The object's dbref, formatted <c>#N</c>.</param>
/// <param name="Name">The object's name.</param>
/// <param name="Type">PLAYER, THING, ROOM or EXIT.</param>
/// <param name="Owner">The owner's name and dbref, formatted <c>Name(#N)</c>.</param>
/// <param name="Flags">Flag names set on the object.</param>
public sealed record ObjectSummaryDto(
	string Dbref,
	string Name,
	string Type,
	string Owner,
	IReadOnlyList<string> Flags);

/// <param name="Name">The full attribute name, backtick-separated for a tree leaf.</param>
/// <param name="Value">The stored value, verbatim — newlines included, nothing substituted.</param>
/// <param name="Flags">Attribute flag names.</param>
public sealed record AttributeDto(
	string Name,
	string Value,
	IReadOnlyList<string> Flags);

/// <param name="Value">
/// The value to store, verbatim. An empty value clears the attribute unless
/// <c>Attribute.EmptyAttributes</c> is enabled, matching what <c>&amp;ATTR obj=</c> does.
/// </param>
public sealed record SetAttributeRequest(string Value);

/// <param name="Flag">The attribute flag name to set.</param>
public sealed record SetAttributeFlagRequest(string Flag);

/// <param name="Name">The new object's name.</param>
/// <param name="Type">THING, ROOM or EXIT. Players are created through account character creation.</param>
/// <param name="Destination">For an EXIT, where it leads. Ignored for other types.</param>
public sealed record CreateObjectRequest(string Name, string Type, string? Destination = null);

/// <param name="Dbref">The created object's dbref, formatted <c>#N</c>.</param>
public sealed record CreatedObjectDto(string Dbref);

/// <param name="Error">The engine's own refusal message.</param>
public sealed record ApiErrorDto(string Error);
