using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A content object, or none. Found contents are one case, so <c>x is AnySharpContent found</c> binds
/// the content.
/// </summary>
[Union]
public sealed class AnyOptionalSharpContent : IUnion
{
	public AnyOptionalSharpContent(AnySharpContent value) => Value = value;
	public AnyOptionalSharpContent(None value) => Value = value;

	public static implicit operator AnyOptionalSharpContent(SharpPlayer value) => new(new AnySharpContent(value));
	public static implicit operator AnyOptionalSharpContent(SharpExit value) => new(new AnySharpContent(value));
	public static implicit operator AnyOptionalSharpContent(SharpThing value) => new(new AnySharpContent(value));

	public object? Value { get; }

	/// <summary>
	/// Equal when both are none, or both hold contents <see cref="AnySharpContent"/> calls equal: the
	/// same model instance.
	/// </summary>
	public override bool Equals(object? obj) => obj is AnyOptionalSharpContent other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => this is AnySharpContent and SharpPlayer;
	public bool IsExit => this is AnySharpContent and SharpExit;
	public bool IsThing => this is AnySharpContent and SharpThing;
	public bool IsNone => this is None;

	public AnyOptionalSharpObject WithRoomOption() => this switch
	{
		AnySharpContent found => found.WithRoomOption(),
		None none => none
	};

}
