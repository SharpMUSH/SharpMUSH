using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Extensions;

/// <summary>
/// Method forms of members the object unions expose as properties. A type cannot declare a property and
/// a method with the same name, so these stay extensions.
/// </summary>
public static class ObjectUnionExtensions
{
	public static AnySharpObject Known(this AnyOptionalSharpObject union)
		=> union.IsNone ? throw new ArgumentNullException(nameof(union)) : union.Known;

	public static bool IsNone(this AnyOptionalSharpObject union) => union.IsNone;

	public static bool IsNone(this AnyOptionalSharpContainer union) => union.IsNone;

	public static bool IsNone(this AnyOptionalSharpObjectOrError union) => union.IsNone;

	public static bool IsError(this AnyOptionalSharpObjectOrError union) => union.IsError;
}
