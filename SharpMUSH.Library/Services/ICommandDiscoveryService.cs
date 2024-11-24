using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface ICommandDiscoveryService
{
		public void InvalidateCache(DBRef DBReference);

		public Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>> MatchUserDefinedCommand(
			IMUSHCodeParser parser,
			IEnumerable<AnySharpObject> objects,
			MString commandString);
}
