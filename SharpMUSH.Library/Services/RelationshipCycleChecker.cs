using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IRelationshipCycleChecker"/>
public sealed class RelationshipCycleChecker(IObjectStore objects) : IRelationshipCycleChecker
{
	public ValueTask<RelationshipSafety> SafeToAddParentAsync(AnySharpObject start, AnySharpObject newParent,
		CancellationToken cancellationToken = default)
		=> SafeToAddAsync(objects, start, newParent, cancellationToken);

	public async ValueTask<bool> SafeToAddZoneAsync(AnySharpObject start, AnySharpObject newZone,
		CancellationToken cancellationToken = default)
		=> await SafeToAddAsync(objects, start, newZone, cancellationToken) == RelationshipSafety.Safe;

	/// <summary>
	/// The rule itself: the same object is a self-reference; otherwise adding the edge closes a cycle
	/// exactly when <paramref name="start"/> is already reachable from <paramref name="newRelated"/>
	/// over parent and zone edges (<c>start -&gt; newRelated -&gt; ... -&gt; start</c>).
	/// </summary>
	internal static async ValueTask<RelationshipSafety> SafeToAddAsync(IObjectStore objects, AnySharpObject start,
		AnySharpObject newRelated, CancellationToken cancellationToken)
	{
		if (start.Object().DBRef.Number == newRelated.Object().DBRef.Number)
		{
			return RelationshipSafety.SelfReference;
		}

		return await objects.IsReachableViaParentOrZoneAsync(newRelated, start, cancellationToken: cancellationToken)
			? RelationshipSafety.Cycle
			: RelationshipSafety.Safe;
	}
}
