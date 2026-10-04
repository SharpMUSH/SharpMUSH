using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The parent/zone cycle guard: whether giving an object a new parent or zone would make it its own
/// ancestor through the combined parent and zone chains. A service depends on this rather than on
/// <see cref="IObjectStore"/>, which is the only store the guard reads.
/// </summary>
public interface IRelationshipCycleChecker
{
	/// <summary>
	/// Whether <paramref name="newParent"/> may become <paramref name="start"/>'s parent. Tells
	/// self-reference from a cycle apart, because PennMUSH's <c>do_parent</c> words them differently
	/// (<c>src/set.c:1432,1477</c>).
	/// </summary>
	ValueTask<RelationshipSafety> SafeToAddParentAsync(AnySharpObject start, AnySharpObject newParent,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Whether <paramref name="newZone"/> may become <paramref name="start"/>'s zone. Collapsed to a bool;
	/// see <see cref="HelperFunctions.SafeToAddZone"/> for why.
	/// </summary>
	ValueTask<bool> SafeToAddZoneAsync(AnySharpObject start, AnySharpObject newZone,
		CancellationToken cancellationToken = default);
}
