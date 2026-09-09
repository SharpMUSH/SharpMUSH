using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Reality;

/// <summary>The room-contents visibility rules shared by look and web projections.</summary>
public static class WorldVisibility
{
	/// <summary>One bounded scan: receiver and container state are captured once; each item remains freshly checked.</summary>
	public static async ValueTask<Func<AnySharpContent, CancellationToken, ValueTask<bool>>> CreateScanAsync(
		AnySharpObject viewer, AnySharpObject container, IRealityPolicy reality,
		IConnectionService connections, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		Func<DBRef, CancellationToken, ValueTask<bool>> perceive = reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(viewer.Object().DBRef, ct)
			: (target, token) => reality.CanPerceiveAsync(viewer.Object().DBRef, target, token);
		var containerVisible = await perceive(container.Object().DBRef, ct);
		var roomLight = containerVisible && container.IsRoom && await container.IsLight();
		var roomDark = containerVisible && container.IsRoom && await container.IsDarkLegal();
		var seeAll = containerVisible && await viewer.IsSee_All();
		return async (item, token) =>
		{
			token.ThrowIfCancellationRequested();
			var target = item.WithRoomOption();
			if (!containerVisible || !await perceive(target.Object().DBRef, token)) return false;
			var dark = await target.IsDarkLegal();
			var light = await target.IsLight();
			var visible = roomLight || (roomDark ? seeAll || light : !dark || seeAll);
			if (!visible || item.IsExit && dark && !seeAll) return false;
			token.ThrowIfCancellationRequested();
			return !item.IsPlayer || await connections.IsOnline(target);
		};
	}

	public static async ValueTask<bool> CanSeeContentAsync(AnySharpObject viewer, AnySharpObject container,
		AnySharpContent item, IRealityPolicy reality, IConnectionService connections, CancellationToken ct = default)
		=> await (await CreateScanAsync(viewer, container, reality, connections, ct))(item, ct);
}
