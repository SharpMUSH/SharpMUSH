using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object, none, or the error that stopped the lookup. Found objects are one case, so
/// <c>x is AnySharpObject found</c> binds the object.
/// </summary>
[Union]
public sealed class AnyOptionalSharpObjectOrError : IUnion
{
	public AnyOptionalSharpObjectOrError(AnySharpObject value) => Value = value;
	public AnyOptionalSharpObjectOrError(None value) => Value = value;
	public AnyOptionalSharpObjectOrError(Error<string> value) => Value = value;

	public static implicit operator AnyOptionalSharpObjectOrError(SharpPlayer value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObjectOrError(SharpRoom value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObjectOrError(SharpExit value) => new(new AnySharpObject(value));
	public static implicit operator AnyOptionalSharpObjectOrError(SharpThing value) => new(new AnySharpObject(value));

	public object? Value { get; }

	/// <summary>
	/// Equal when both hold equal errors, are both none, or hold objects <see cref="AnySharpObject"/>
	/// calls equal: the same model instance.
	/// </summary>
	public override bool Equals(object? obj) => obj is AnyOptionalSharpObjectOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => this is AnySharpObject and SharpPlayer;
	public bool IsRoom => this is AnySharpObject and SharpRoom;
	public bool IsExit => this is AnySharpObject and SharpExit;
	public bool IsThing => this is AnySharpObject and SharpThing;
	public bool IsNone => this is None;
	public bool IsError => this is Error<string>;

	public SharpPlayer AsPlayer => this is AnySharpObject found ? found.AsPlayer : throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => this is AnySharpObject found ? found.AsRoom : throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpExit AsExit => this is AnySharpObject found ? found.AsExit : throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => this is AnySharpObject found ? found.AsThing : throw UnionCase.Mismatch<SharpThing>(Value);

	public bool IsAnyObject => this is AnySharpObject;

	/// <summary>True when this names an object: neither <see cref="None"/> nor an error.</summary>
	public bool IsValid() => IsAnyObject;

	public AnySharpObject AsAnyObject => this switch
	{
		AnySharpObject found => found,
		None or Error<string> => throw new ArgumentOutOfRangeException()
	};

	public None AsNone => this is None none ? none : throw UnionCase.Mismatch<None>(Value);
	public Error<string> AsError => this is Error<string> error ? error : throw UnionCase.Mismatch<Error<string>>(Value);

	public AnyOptionalSharpObject WithoutError() => this switch
	{
		AnySharpObject found => found,
		None none => none,
		Error<string> => throw new ArgumentException("Cannot convert an Error to a non-Error value.")
	};
}
