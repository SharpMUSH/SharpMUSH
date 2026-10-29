using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Changes an object's owner, parent and zone.</summary>
public interface IObjectRelationshipService
{
	ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify);

	/// <summary>Whether <paramref name="executor"/> may give <paramref name="obj"/> to <paramref name="newOwner"/>: PennMUSH's <c>chown_ok</c>.</summary>
	ValueTask<bool> CanChown(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner);

	/// <summary><see cref="CanChown"/>, saying which part of <c>chown_ok</c> refused.</summary>
	ValueTask<Permission> ChownPermission(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner);

	ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent, bool notify);

	ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify);

	ValueTask<CallState> SetZone(AnySharpObject executor, AnySharpObject obj, AnySharpObject newZone, bool notify);

	ValueTask<CallState> UnsetZone(AnySharpObject executor, AnySharpObject obj, bool notify);
}
