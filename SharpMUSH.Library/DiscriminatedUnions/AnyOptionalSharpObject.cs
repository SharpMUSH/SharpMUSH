using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object, or none. Found objects are one case, so <c>x is AnySharpObject found</c> binds the
/// object and <c>x is AnySharpObject and SharpPlayer player</c> binds one kind of it.
/// </summary>
[Union]
public sealed class AnyOptionalSharpObject : IUnion, IObjectShaped<AnyOptionalSharpObject>
{
	public AnyOptionalSharpObject(AnySharpObject value) => Value = value;
	public AnyOptionalSharpObject(None value) => Value = value;

	public static implicit operator AnyOptionalSharpObject(SharpPlayer value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObject(SharpRoom value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObject(SharpExit value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObject(SharpThing value) => new(new AnySharpObject(value));

	public object? Value { get; }

	/// <summary>
	/// Equal when both are none, or both hold objects <see cref="AnySharpObject"/> calls equal: the same
	/// model instance.
	/// </summary>
	public override bool Equals(object? obj) => obj is AnyOptionalSharpObject other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => this is AnySharpObject and SharpPlayer;
	public bool IsRoom => this is AnySharpObject and SharpRoom;
	public bool IsExit => this is AnySharpObject and SharpExit;
	public bool IsThing => this is AnySharpObject and SharpThing;
	public bool IsNone => this is None;

	public SharpPlayer AsPlayer => this is AnySharpObject found ? found.AsPlayer : throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => this is AnySharpObject found ? found.AsRoom : throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpExit AsExit => this is AnySharpObject found ? found.AsExit : throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => this is AnySharpObject found ? found.AsThing : throw UnionCase.Mismatch<SharpThing>(Value);

	public AnySharpObject Known => this switch
	{
		AnySharpObject found => found,
		None => throw new ArgumentOutOfRangeException()
	};

	public SharpObject? Object() => this switch
	{
		AnySharpObject found => found.Object(),
		None => null
	};

	public string? Id() => this switch
	{
		AnySharpObject found => found.Id(),
		None => null
	};

	public AnyOptionalSharpObjectOrError WithErrorOption() => this switch
	{
		AnySharpObject found => found,
		None none => none
	};

	public AnySharpObject WithoutNone() => this switch
	{
		AnySharpObject found => found,
		None => throw new ArgumentException("Cannot convert an None to a non-None value.")
	};

	public static DBRef? RefOf(AnyOptionalSharpObject value) => value switch
	{
		AnySharpObject found => found.Object().DBRef,
		None => null
	};

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpObject value)
	{
		value = node;
		return true;
	}
}
