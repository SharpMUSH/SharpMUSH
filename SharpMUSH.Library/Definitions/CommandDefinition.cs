using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

public record struct CommandDefinition(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command)
{
	public static implicit operator (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command)(CommandDefinition value)
	{
		return (value.Attribute, value.Command);
	}

	public static implicit operator CommandDefinition((SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command) value)
	{
		return new CommandDefinition(value.Attribute, value.Command);
	}
}