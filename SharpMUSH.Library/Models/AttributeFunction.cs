using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// An attribute fetched to be run as a function: the object it was read from, its full name, and its
/// code. <see cref="Owner"/> is who the code runs as, and <see cref="Dialect"/> the dialect it is written in
/// (<see cref="Softcode.SoftcodeDialects"/>), or <c>null</c> for the game's.
/// </summary>
public readonly record struct AttributeFunction(AnySharpObject Owner, string Name, MString Code, string? Dialect = null)
{
	/// <summary><paramref name="attribute"/>, read from <paramref name="owner"/>, to be run as a function.</summary>
	public static AttributeFunction Of(AnySharpObject owner, SharpAttribute attribute)
		=> new(owner, attribute.LongName.ToUpperInvariant(), attribute.Value, Softcode.SoftcodeDialects.Of(attribute));
}
