using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The numbers the game cannot do without: God and every object the configuration names. They are never destroyed
/// (<see cref="Interfaces.IObjectDestructionService.IsSpecialObject"/>) and, when free, never handed out unasked
/// (<see cref="IFreeNumberStore.SetProtectedNumbersAsync"/>).
/// </summary>
public static class SpecialObjectNumbers
{
	private const int GodDbRefNumber = 1;

	/// <summary>
	/// PennMUSH's <c>special_object()</c> list (God, player_start, master_room, base_room, default_home, probate_judge),
	/// widened to the objects PennMUSH leaves destroyable: the ancestors and the event, HTTP and package handlers.
	/// </summary>
	public static IReadOnlySet<int> From(SharpMUSHOptions options)
	{
		var database = options.Database;
		uint?[] named =
		[
			database.PlayerStart, database.MasterRoom, database.BaseRoom, database.DefaultHome, options.Command.ProbateJudge,
			database.AncestorRoom, database.AncestorExit, database.AncestorThing, database.AncestorPlayer,
			database.EventHandler, database.HttpHandler, database.PackageManager
		];

		return named.OfType<uint>().Where(number => number <= int.MaxValue).Select(number => (int)number)
			.Append(GodDbRefNumber).ToHashSet();
	}
}
