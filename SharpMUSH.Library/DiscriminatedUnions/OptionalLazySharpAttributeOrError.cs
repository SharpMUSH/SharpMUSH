using System.Runtime.CompilerServices;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class OptionalLazySharpAttributeOrError : IUnion
{
	public OptionalLazySharpAttributeOrError(LazySharpAttribute[] value) => Value = value;
	public OptionalLazySharpAttributeOrError(None value) => Value = value;
	public OptionalLazySharpAttributeOrError(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is OptionalLazySharpAttributeOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsAttribute => Value is LazySharpAttribute[];
	public bool IsNone => Value is None;
	public bool IsError => Value is Error<string>;

	public async ValueTask<CallState> AsCallStateAsync() => this switch
	{
		LazySharpAttribute[] attributes => await attributes.Last().Value.WithCancellation(CancellationToken.None),
		None => ErrorMessages.Returns.NoSuchAttribute,
		Error<string> error => error.Value
	};
}
