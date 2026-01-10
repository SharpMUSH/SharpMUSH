using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Commands.ListenPattern;

/// <summary>
/// Command to execute a listen pattern action attribute.
/// This command is triggered when a @listen pattern or ^-listen pattern matches.
/// </summary>
/// <param name="Listener">The object with the listen pattern</param>
/// <param name="Speaker">The object that spoke the message</param>
/// <param name="AttributeName">The action attribute to execute (AHEAR, AMHEAR, AAHEAR, or ^-listen attribute)</param>
/// <param name="Registers">Parser registers to set (%0-%9 for captures, %# for speaker, %! for listener)</param>
public record ExecuteListenPatternCommand(
	AnySharpObject Listener,
	AnySharpObject Speaker,
	string AttributeName,
	Dictionary<string, CallState> Registers
) : ICommand;
