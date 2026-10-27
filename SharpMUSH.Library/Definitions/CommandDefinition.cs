using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

public record struct CommandDefinition(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command)
{
	/// <summary>
	/// The switches an alias carries (<c>@sudo</c> for <c>@force/inplace</c>), put before any the player types. Empty
	/// for a command reached by its own name.
	/// </summary>
	public string[] ImpliedSwitches { get => field ?? []; init; }

	public static implicit operator (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command)(CommandDefinition value)
		=> (value.Attribute, value.Command);

	public static implicit operator CommandDefinition((SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Command) value)
		=> new(value.Attribute, value.Command);
}