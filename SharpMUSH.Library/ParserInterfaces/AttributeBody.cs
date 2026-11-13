using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// Stored attribute text a frame runs, and which version of the attribute it is: the parse cache keys its tree on
/// these instead of on the text, which would have to be hashed in full on every call.
/// </summary>
/// <param name="Object">The object the attribute was read for.</param>
/// <param name="Attribute">The attribute's full name.</param>
/// <param name="Modified">The attribute's modification time (<see cref="SharpAttribute.ModifiedTime"/>), which every
/// write moves forward.</param>
/// <param name="Text">The text run: the attribute's value, or the action list of a <c>$</c>-command in it. The cache
/// is used only for a parse of this exact instance.</param>
public sealed record AttributeBody(DBRef Object, string Attribute, long Modified, MString Text)
{
	/// <summary>
	/// <paramref name="body"/>, as a body, read from <paramref name="attribute"/> on <paramref name="obj"/>; null when
	/// the attribute has no modification time.
	/// </summary>
	public static AttributeBody? Of(DBRef obj, SharpAttribute attribute, MString body)
		=> attribute.ModifiedTime is { } modified ? new(obj, attribute.LongName, modified, body) : null;
}
