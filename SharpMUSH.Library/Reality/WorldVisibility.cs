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
		var scan = await CreateFlagScanAsync(viewer, container, reality, connections, ct);
		return async (item, token) => await scan(item, token) is SharpObjectFlag[];
	}

	/// <summary>
	/// <see cref="CreateScanAsync"/> that hands back each visible item's own flags, read once for the
	/// DARK/LIGHT rules, so a listing that shows them does not read them again.
	/// </summary>
	public static async ValueTask<Func<AnySharpContent, CancellationToken, ValueTask<Found<SharpObjectFlag[]>>>> CreateFlagScanAsync(
		AnySharpObject viewer, AnySharpObject container, IRealityPolicy reality,
		IConnectionService connections, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		Func<DBRef, CancellationToken, ValueTask<bool>> perceive = reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(viewer.Object().DBRef, ct)
			: (target, token) => reality.CanPerceiveAsync(viewer.Object().DBRef, target, token);
		var containerVisible = await perceive(container.Object().DBRef, ct);
		SharpObjectFlag[] roomFlags = containerVisible && container.IsRoom ? await ReadAsync(() => FlagsOf(container), ct) : [];
		var roomLight = HelperFunctions.HasFlag(roomFlags, "LIGHT");
		var roomDark = HelperFunctions.HasFlag(roomFlags, "DARK")
			&& await ReadAsync(() => container.IsDarkLegal(roomFlags, ExecutionBudget.CurrentToken), ct);
		var seeAll = containerVisible && await ReadAsync(() => viewer.IsSee_All(), ct);
		return async (item, token) =>
		{
			token.ThrowIfCancellationRequested();
			var target = item.WithRoomOption();
			if (!containerVisible || !await perceive(target.Object().DBRef, token)) return new NotFound();
			// One read of the item's flags answers DARK and LIGHT both; it was a read per question.
			var flags = await ReadAsync(() => FlagsOf(target), token);
			var dark = HelperFunctions.HasFlag(flags, "DARK")
				&& await ReadAsync(() => target.IsDarkLegal(flags, ExecutionBudget.CurrentToken), token);
			var light = HelperFunctions.HasFlag(flags, "LIGHT");
			var visible = roomLight || (roomDark ? seeAll || light : !dark || seeAll);
			if (!visible || item.IsExit && dark && !seeAll) return new NotFound();
			token.ThrowIfCancellationRequested();
			if (item.IsPlayer && !await ReadAsync(() => connections.IsOnline(target), token)) return new NotFound();
			return flags;
		};
	}

	private static ValueTask<SharpObjectFlag[]> FlagsOf(AnySharpObject obj)
		=> obj.Object().FlagsNowAsync(ExecutionBudget.CurrentToken);

	// Visibility helpers and the connection interface retain their published tokenless signatures.
	// Each read gets the caller's lifetime, including delegate invocations after scan creation ends.
	// A caller already running under that lifetime (look passes the ambient token) gets no new budget
	// or linked source: those were most of what a full room allocated per item.
	private static async ValueTask<T> ReadAsync<T>(Func<ValueTask<T>> read, CancellationToken ct)
	{
		using var scope = ExecutionBudget.EnterLinked(ct);
		var pending = read();
		return pending.IsCompletedSuccessfully
			? pending.Result
			: await pending.AsTask().WaitAsync(ExecutionBudget.CurrentToken);
	}

	public static async ValueTask<bool> CanSeeContentAsync(AnySharpObject viewer, AnySharpObject container,
		AnySharpContent item, IRealityPolicy reality, IConnectionService connections, CancellationToken ct = default)
		=> await (await CreateScanAsync(viewer, container, reality, connections, ct))(item, ct);
}
