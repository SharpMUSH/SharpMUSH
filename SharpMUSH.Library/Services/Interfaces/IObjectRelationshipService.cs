using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Changes an object's owner, parent and zone.</summary>
public interface IObjectRelationshipService
{
	ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify);

	ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent, bool notify);

	ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify);

	ValueTask<CallState> SetZone(AnySharpObject executor, AnySharpObject obj, AnySharpObject newZone, bool notify);

	ValueTask<CallState> UnsetZone(AnySharpObject executor, AnySharpObject obj, bool notify);
}
