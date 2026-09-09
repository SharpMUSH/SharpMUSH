using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Reality;

/// <summary>The room-contents visibility rules shared by look and web projections.</summary>
public static class WorldVisibility
{
	public static async ValueTask<bool> CanSeeContentAsync(AnySharpObject viewer, AnySharpObject container,
		AnySharpContent item, IRealityPolicy reality, IConnectionService connections, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var target = item.WithRoomOption();
		if (!await reality.CanPerceiveAsync(viewer.Object().DBRef, container.Object().DBRef, ct)
			|| !await reality.CanPerceiveAsync(viewer.Object().DBRef, target.Object().DBRef, ct)) return false;

		var roomLight = container.IsRoom && await container.IsLight();
		var roomDark = container.IsRoom && await container.IsDarkLegal();
		var seeAll = await viewer.IsSee_All();
		var dark = await target.IsDarkLegal();
		var light = await target.IsLight();
		var visible = roomLight || (roomDark ? seeAll || light : !dark || seeAll);
		if (!visible || item.IsExit && dark && !seeAll) return false;
		ct.ThrowIfCancellationRequested();
		return !item.IsPlayer || await connections.IsOnline(target);
	}
}
