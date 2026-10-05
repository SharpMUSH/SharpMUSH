namespace SharpMUSH.Library.Models;

/// <summary>
/// How many objects of each type there are — in the whole database, or owned by one player. What
/// PennMUSH's <c>do_stats</c> and <c>fun_lstats</c> report, counted by the store rather than by walking
/// the objects.
/// </summary>
public readonly record struct ObjectTypeCounts(int Rooms, int Exits, int Things, int Players)
{
	public int Total => Rooms + Exits + Things + Players;
}
