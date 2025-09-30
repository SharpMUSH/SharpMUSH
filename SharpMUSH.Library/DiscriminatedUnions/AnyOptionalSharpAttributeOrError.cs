using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class OptionalSharpAttributeOrError(OneOf<SharpAttribute[], None, Error<string>> input)
	: OneOfBase<SharpAttribute[], None, Error<string>>(input)
{
	public static implicit operator OptionalSharpAttributeOrError(SharpAttribute[] x) => new(x);
	public static implicit operator OptionalSharpAttributeOrError(None x) => new(x);
	public static implicit operator OptionalSharpAttributeOrError(Error<string> x) => new(x);

	public bool IsAttribute => IsT0;
	public bool IsNone => IsT1;
	public bool IsError => IsT2;

	public SharpAttribute[] AsAttribute => AsT0;
	public Error<string> AsError => AsT2;

	public CallState AsCallStateError => IsT1
		? new CallState(Errors.ErrorNoSuchAttribute)
		: new CallState(AsT2.Value);

	public CallState AsCallState => Match(
		attribute => new CallState(AsT0.Last().Value),
		none => new CallState(Errors.ErrorNoSuchAttribute),
		error => new CallState(AsT2.Value));
}