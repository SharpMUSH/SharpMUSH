using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

public record struct FunctionDefinition(SharpFunctionAttribute Attribute, Func<IMUSHCodeParser, ValueTask<CallState>> Function)
{
	public static implicit operator (SharpFunctionAttribute Attribute, Func<IMUSHCodeParser, ValueTask<CallState>> Function)(FunctionDefinition value) => 
		(value.Attribute, value.Function);

	public static implicit operator FunctionDefinition((SharpFunctionAttribute Attribute, Func<IMUSHCodeParser, ValueTask<CallState>> Function) value) 
		=> new(value.Attribute, value.Function);
}