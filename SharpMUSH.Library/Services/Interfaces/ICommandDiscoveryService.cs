using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface ICommandDiscoveryService
{
	/// <summary>
	/// The <c>$</c>-commands of <paramref name="objects"/> (each with its @parent chain) that match
	/// <paramref name="commandString"/> and whose object lets <paramref name="player"/> pass its
	/// @lock/command and @lock/use. Each object that matched but refused is added to
	/// <paramref name="lockFailures"/> when one is given.
	/// </summary>
	ValueTask<Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>> MatchUserDefinedCommand(
		IMUSHCodeParser parser,
		IAsyncEnumerable<AnySharpObject> objects,
		MString commandString,
		AnySharpObject player,
		ICollection<AnySharpObject>? lockFailures = null);
}
