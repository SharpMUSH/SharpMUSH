using System.Collections.Immutable;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// A privilege ceiling over the flow it is entered on: while it holds, the objects it names keep only what their
/// roles at or below <see cref="Priority"/> grant (<see cref="ObjectGrants.AtMost"/>). It never adds a permission and
/// never makes one object control another; it only takes away. RhostMUSH's <c>ueval()</c> and <c>streval()</c> enter
/// one for the code they evaluate.
/// </summary>
/// <param name="Priority">The highest role priority whose grants still count.</param>
/// <param name="Objects">The dbref numbers the ceiling lowers.</param>
public sealed record PrivilegeCeiling(int Priority, ImmutableHashSet<int> Objects)
{
	private static readonly AsyncLocal<ImmutableList<PrivilegeCeiling>?> Ambient = new();

	/// <summary>
	/// The lowest ceiling over <paramref name="number"/> on this flow, or null when nothing lowers it. Ceilings
	/// entered inside one another all hold, so the lowest decides.
	/// </summary>
	public static int? Over(int number)
	{
		int? lowest = null;
		foreach (var ceiling in Ambient.Value ?? [])
			if (ceiling.Objects.Contains(number) && (lowest is null || ceiling.Priority < lowest))
				lowest = ceiling.Priority;
		return lowest;
	}

	/// <summary>Until disposed, this ceiling holds on this flow, as well as any already entered.</summary>
	public IDisposable Enter()
	{
		var previous = Ambient.Value;
		Ambient.Value = (previous ?? []).Add(this);
		return new Restore(previous);
	}

	private sealed class Restore(ImmutableList<PrivilegeCeiling>? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
