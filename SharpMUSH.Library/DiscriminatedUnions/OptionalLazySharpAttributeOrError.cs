using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class OptionalLazySharpAttributeOrError(OneOf<LazySharpAttribute[], None, Error<string>> input)
	: OneOfBase<LazySharpAttribute[], None, Error<string>>(input)
{
	public static implicit operator OptionalLazySharpAttributeOrError(LazySharpAttribute[] x) => new(x);
	public static implicit operator OptionalLazySharpAttributeOrError(None x) => new(x);
	public static implicit operator OptionalLazySharpAttributeOrError(Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsNone => IsT1;
	public bool IsError => IsT2;

	public LazySharpAttribute[] AsAttribute => AsT0;
	public Error<string> AsError => AsT2;

	public CallState AsCallStateError => IsT1
		? new CallState(Errors.ErrorNoSuchAttribute)
		: new CallState(AsT2.Value);

	public async ValueTask<CallState> AsCallStateAsync() => await Match<ValueTask<CallState>>(
			async attributes => await attributes.Last().Value.WithCancellation(CancellationToken.None),
			none => ValueTask.FromResult<CallState>(Errors.ErrorNoSuchAttribute),
			error => ValueTask.FromResult<CallState>(AsT2.Value));
}