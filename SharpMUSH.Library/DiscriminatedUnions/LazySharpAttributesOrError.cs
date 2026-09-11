using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class LazySharpAttributesOrError : IUnion
{
	public LazySharpAttributesOrError(IAsyncEnumerable<LazySharpAttribute> value) => Value = value;
	public LazySharpAttributesOrError(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is LazySharpAttributesOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public static LazySharpAttributesOrError FromAsync(IAsyncEnumerable<LazySharpAttribute> x) => new(x);

	public bool IsAttribute => Value is IAsyncEnumerable<LazySharpAttribute>;
	public bool IsError => Value is Error<string>;

	public IAsyncEnumerable<LazySharpAttribute> AsAttributes => Value as IAsyncEnumerable<LazySharpAttribute>
		?? throw UnionCase.Mismatch<IAsyncEnumerable<LazySharpAttribute>>(Value);

	public Error<string> AsError => Value is Error<string> error ? error : throw UnionCase.Mismatch<Error<string>>(Value);
}
