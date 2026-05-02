using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpAttribute[] and SharpError.
/// Replaces SharpAttributesOrError : OneOfBase&lt;SharpAttribute[], Error&lt;string&gt;&gt;.
/// </summary>
public union SharpAttributesOrError(SharpAttribute[], SharpError)
{
	public bool IsAttribute => Value is SharpAttribute[];
	public bool IsError     => Value is SharpError;

	public SharpAttribute[] AsAttributes => (SharpAttribute[])Value!;
	public SharpError       AsError       => (SharpError)Value!;
}
