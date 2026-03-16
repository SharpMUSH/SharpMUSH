namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Warning type bit flags for topology checking system.
/// Based on PennMUSH's warning system.
/// </summary>
[Flags]
public enum WarningType : uint
{
	/// <summary>
	/// No warnings
	/// </summary>
	None = 0,

	// Lock-related warnings (used internally for message checks)
	/// <summary>
	/// Check for unlocked-object warnings
	/// </summary>
	Unlocked = 0x1,

	/// <summary>
	/// Check for locked-object warnings
	/// </summary>
	Locked = 0x2,

	// Exit-specific warnings
	/// <summary>
	/// Find one-way exits (no return exit)
	/// </summary>
	ExitOneway = 0x1,

	/// <summary>
	/// Find multiple exits to the same place
	/// </summary>
	ExitMultiple = 0x2,

	/// <summary>
	/// Find exits without messages (SUCCESS, OSUCCESS, ODROP, FAILURE)
	/// </summary>
	ExitMsgs = 0x4,

	/// <summary>
	/// Find exits without descriptions
	/// </summary>
	ExitDesc = 0x8,

	/// <summary>
	/// Find unlinked exits (can be stolen)
	/// </summary>
	ExitUnlinked = 0x10,

	// Thing-specific warnings
	/// <summary>
	/// Find things without messages (SUCCESS, OSUCCESS, DROP, ODROP, FAILURE)
	/// </summary>
	ThingMsgs = 0x100,

	/// <summary>
	/// Find things without descriptions
	/// </summary>
	ThingDesc = 0x200,

	// Room-specific warnings
	/// <summary>
	/// Find rooms without descriptions
	/// </summary>
	RoomDesc = 0x1000,

	// Player-specific warnings
	/// <summary>
	/// Find players without descriptions
	/// </summary>
	PlayerDesc = 0x10000,

	// General warnings
	/// <summary>
	/// Find bad locks (invalid references, garbage objects, etc.)
	/// </summary>
	LockProbs = 0x100000,

	// Convenience groups
	/// <summary>
	/// Serious warnings only: unlinked exits, missing descriptions, bad locks
	/// </summary>
	Serious = ExitUnlinked | ThingDesc | RoomDesc | PlayerDesc | LockProbs,

	/// <summary>
	/// Standard warnings (default): serious warnings plus exit topology and messages
	/// </summary>
	Normal = Serious | ExitOneway | ExitMultiple | ExitMsgs,

	/// <summary>
	/// Extra warnings: normal warnings plus thing messages
	/// </summary>
	Extra = Normal | ThingMsgs,

	/// <summary>
	/// All warnings: everything including exit descriptions
	/// </summary>
	All = Extra | ExitDesc
}

/// <summary>
/// Helper class for working with warning types
/// </summary>
public static class WarningTypeHelper
{
	/// <summary>
	/// Mapping of warning names to their flag values
	/// </summary>
	private static readonly Dictionary<string, WarningType> WarningNames = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "none", WarningType.None },
		{ "exit-unlinked", WarningType.ExitUnlinked },
		{ "thing-desc", WarningType.ThingDesc },
		{ "room-desc", WarningType.RoomDesc },
		{ "my-desc", WarningType.PlayerDesc },
		{ "exit-oneway", WarningType.ExitOneway },
		{ "exit-multiple", WarningType.ExitMultiple },
		{ "exit-msgs", WarningType.ExitMsgs },
		{ "thing-msgs", WarningType.ThingMsgs },
		{ "exit-desc", WarningType.ExitDesc },
		{ "lock-checks", WarningType.LockProbs },
		{ "serious", WarningType.Serious },
		{ "normal", WarningType.Normal },
		{ "extra", WarningType.Extra },
		{ "all", WarningType.All }
	};

	/// <summary>
	/// Order of warnings for unparsing (most comprehensive first)
	/// </summary>
	private static readonly (string Name, WarningType Flag)[] UnparseOrder =
	[
		("all", WarningType.All),
		("extra", WarningType.Extra),
		("normal", WarningType.Normal),
		("serious", WarningType.Serious),
		("lock-checks", WarningType.LockProbs),
		("exit-desc", WarningType.ExitDesc),
		("thing-msgs", WarningType.ThingMsgs),
		("exit-msgs", WarningType.ExitMsgs),
		("exit-multiple", WarningType.ExitMultiple),
		("exit-oneway", WarningType.ExitOneway),
		("my-desc", WarningType.PlayerDesc),
		("room-desc", WarningType.RoomDesc),
		("thing-desc", WarningType.ThingDesc),
		("exit-unlinked", WarningType.ExitUnlinked),
		("none", WarningType.None)
	];

	/// <summary>
	/// Parse a space-separated list of warning names into a WarningType bitmask
	/// </summary>
	/// <param name="warningList">Space-separated warning names, can use ! prefix to negate</param>
	/// <param name="unknownWarnings">List to collect any unknown warning names</param>
	/// <returns>The combined warning flags</returns>
	public static WarningType ParseWarnings(string warningList, List<string>? unknownWarnings = null)
	{
		if (string.IsNullOrWhiteSpace(warningList))
		{
			return WarningType.None;
		}

		var flags = WarningType.None;
		var negateFlags = WarningType.None;

		var warnings = warningList.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (var warning in warnings)
		{
			var isNegated = warning.StartsWith('!');
			var warningName = isNegated ? warning[1..] : warning;

			if (WarningNames.TryGetValue(warningName, out var flag))
			{
				if (isNegated)
				{
					negateFlags |= flag;
				}
				else
				{
					flags |= flag;
				}
			}
			else
			{
				unknownWarnings?.Add(warning);
			}
		}

		return flags & ~negateFlags;
	}

	/// <summary>
	/// Convert a warning bitmask to a space-separated string of warning names
	/// </summary>
	/// <param name="warnings">The warning flags to unparse</param>
	/// <returns>Space-separated warning names</returns>
	public static string UnparseWarnings(WarningType warnings)
	{
		if (warnings == WarningType.None)
		{
			return "none";
		}

		var result = new List<string>();

		foreach (var (name, flag) in UnparseOrder)
		{
			if (flag == WarningType.None)
			{
				continue;
			}

			// Check if all bits of this flag are set in warnings
			if ((warnings & flag) == flag)
			{
				result.Add(name);
				// Remove these bits so we don't list subsumed warnings
				warnings &= ~flag;
			}
		}

		return result.Count > 0 ? string.Join(' ', result) : "none";
	}

	/// <summary>
	/// Get all available warning names
	/// </summary>
	public static IEnumerable<string> GetAllWarningNames() => WarningNames.Keys;
}
