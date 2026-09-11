using System.Runtime.CompilerServices;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class OptionalSharpAttributeOrError : IUnion
{
	public OptionalSharpAttributeOrError(SharpAttribute[] value) => Value = value;
	public OptionalSharpAttributeOrError(None value) => Value = value;
	public OptionalSharpAttributeOrError(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is OptionalSharpAttributeOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsAttribute => Value is SharpAttribute[];
	public bool IsNone => Value is None;
	public bool IsError => Value is Error<string>;

	public SharpAttribute[] AsAttribute => Value as SharpAttribute[] ?? throw UnionCase.Mismatch<SharpAttribute[]>(Value);
	public Error<string> AsError => Value is Error<string> error ? error : throw UnionCase.Mismatch<Error<string>>(Value);

	public CallState AsCallStateError => IsNone
		? new CallState(ErrorMessages.Returns.NoSuchAttribute)
		: new CallState(AsError.Value);

	public CallState AsCallState => this switch
	{
		SharpAttribute[] attribute => new CallState(attribute.Last().Value),
		None => new CallState(ErrorMessages.Returns.NoSuchAttribute),
		Error<string> error => new CallState(error.Value)
	};
}
