using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface ICommandDiscoveryService
{
	public ValueTask<Option<Func<IMUSHCodeParser, ValueTask<Option<CallState>>>>> MatchUserDefinedCommand(IMUSHCodeParser parser, AnySharpObject[] objects, MString commandString);
}
