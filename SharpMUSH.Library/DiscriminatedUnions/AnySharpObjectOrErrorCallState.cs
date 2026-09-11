using System.Runtime.CompilerServices;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class AnySharpObjectOrErrorCallState : IUnion
{
	public AnySharpObjectOrErrorCallState(AnySharpObject value) => Value = value;
	public AnySharpObjectOrErrorCallState(Error<CallState> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnySharpObjectOrErrorCallState other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsAnySharpObject => Value is AnySharpObject;
	public bool IsError => Value is Error<CallState>;

	public AnySharpObject AsSharpObject => Value as AnySharpObject ?? throw UnionCase.Mismatch<AnySharpObject>(Value);

	public CallState AsError => Value is Error<CallState> error ? error.Value : throw UnionCase.Mismatch<Error<CallState>>(Value);
}
