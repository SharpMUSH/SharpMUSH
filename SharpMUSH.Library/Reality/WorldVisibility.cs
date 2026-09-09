using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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
		var roomLight = containerVisible && container.IsRoom && await ReadAsync(() => container.IsLight(), ct);
		var roomDark = containerVisible && container.IsRoom && await ReadAsync(() => container.IsDarkLegal(), ct);
		var seeAll = containerVisible && await ReadAsync(() => viewer.IsSee_All(), ct);
		return async (item, token) =>
		{
			token.ThrowIfCancellationRequested();
			var target = item.WithRoomOption();
			if (!containerVisible || !await perceive(target.Object().DBRef, token)) return false;
			var dark = await ReadAsync(() => target.IsDarkLegal(), token);
			var light = await ReadAsync(() => target.IsLight(), token);
			var visible = roomLight || (roomDark ? seeAll || light : !dark || seeAll);
			if (!visible || item.IsExit && dark && !seeAll) return false;
			token.ThrowIfCancellationRequested();
			return !item.IsPlayer || await ReadAsync(() => connections.IsOnline(target), token);
		};
	}

	// Visibility helpers and the connection interface retain their published tokenless signatures.
	// Each read gets the caller's lifetime, including delegate invocations after scan creation ends.
	private static async ValueTask<bool> ReadAsync(Func<ValueTask<bool>> read, CancellationToken ct)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
		using var budget = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		using var scope = budget.Enter();
		budget.ThrowIfExceeded();
		return await read().AsTask().WaitAsync(budget.Token);
	}

	public static async ValueTask<bool> CanSeeContentAsync(AnySharpObject viewer, AnySharpObject container,
		AnySharpContent item, IRealityPolicy reality, IConnectionService connections, CancellationToken ct = default)
		=> await (await CreateScanAsync(viewer, container, reality, connections, ct))(item, ct);
}
