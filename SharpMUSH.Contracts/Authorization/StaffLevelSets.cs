namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The staff roles a game starts with, chosen in the setup wizard: SharpMUSH's starter roles (helper and moderator)
/// or RhostMUSH's levels (<see cref="BuiltInRoles.RhostLevels"/>). The system roles are the same in both.
/// </summary>
public static class StaffLevelSets
{
	public const string SharpMUSH = "sharpmush";
	public const string RhostMUSH = "rhostmush";

	public static bool IsKnown(string set) => set is SharpMUSH or RhostMUSH;
}
