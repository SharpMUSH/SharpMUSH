using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class AnyOptionalSharpObjectOrError : IUnion
{
	public AnyOptionalSharpObjectOrError(SharpPlayer value) => Value = value;
	public AnyOptionalSharpObjectOrError(SharpRoom value) => Value = value;
	public AnyOptionalSharpObjectOrError(SharpExit value) => Value = value;
	public AnyOptionalSharpObjectOrError(SharpThing value) => Value = value;
	public AnyOptionalSharpObjectOrError(None value) => Value = value;
	public AnyOptionalSharpObjectOrError(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnyOptionalSharpObjectOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom => Value is SharpRoom;
	public bool IsExit => Value is SharpExit;
	public bool IsThing => Value is SharpThing;
	public bool IsNone => Value is None;
	public bool IsError => Value is Error<string>;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => Value as SharpRoom ?? throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpExit AsExit => Value as SharpExit ?? throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public bool IsAnyObject => !IsNone && !IsError;

	/// <summary>True when this names an object: neither <see cref="None"/> nor an error.</summary>
	public bool IsValid() => IsAnyObject;

	public AnySharpObject AsAnyObject => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None or Error<string> => throw new ArgumentOutOfRangeException()
	};

	public None AsNone => Value is None none ? none : throw UnionCase.Mismatch<None>(Value);
	public Error<string> AsError => Value is Error<string> error ? error : throw UnionCase.Mismatch<Error<string>>(Value);

	public AnyOptionalSharpObject WithoutError() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpExit exit => exit,
		SharpThing thing => thing,
		None none => none,
		Error<string> => throw new ArgumentException("Cannot convert an Error to a non-Error value.")
	};
}
