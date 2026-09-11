using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class SharpAttributesOrError : IUnion
{
	public SharpAttributesOrError(SharpAttribute[] value) => Value = value;
	public SharpAttributesOrError(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is SharpAttributesOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsAttribute => Value is SharpAttribute[];
	public bool IsError => Value is Error<string>;

	public SharpAttribute[] AsAttributes => Value as SharpAttribute[] ?? throw UnionCase.Mismatch<SharpAttribute[]>(Value);
	public Error<string> AsError => Value is Error<string> error ? error : throw UnionCase.Mismatch<Error<string>>(Value);
}
