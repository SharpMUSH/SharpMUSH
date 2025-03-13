using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpObjectOrErrorCallState(
	OneOf<AnySharpObject,
		OneOf.Types.Error<CallState>> input)
	: OneOfBase<AnySharpObject,
		OneOf.Types.Error<CallState>>(input)
{
	public static implicit operator AnySharpObjectOrErrorCallState(AnySharpObject x) => new(x);
	public static implicit operator AnySharpObjectOrErrorCallState(OneOf.Types.Error<CallState> x) => new(x);

	public bool IsAnySharpObject => IsT0;
	public bool IsError => IsT1;

	public AnySharpObject AsSharpObject => AsT0;

	public CallState AsError => AsT1.Value;
}