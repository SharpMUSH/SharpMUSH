using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The roles a fresh world starts with.
///
/// <para><b>System roles</b> (<see cref="All"/>) cannot be deleted, re-slugged, re-prioritised or
/// assigned by hand: <c>everyone</c> (Discord's @everyone) is held by every active account, and the
/// tier roles follow a character's flags the way PennMUSH and RhostMUSH ranks do. An account holds its
/// highest tier and every tier below it down to <c>player</c> (a Wizard holds wizard, royalty, builder
/// and player), so each tier lists only what it adds. An account without characters holds
/// <c>guest</c>. Their name, colour and permissions stay editable.</para>
///
/// <para><b>Starter roles</b> (<see cref="Starters"/>) are ordinary roles seeded once into a new
/// world, modelled on RhostMUSH's Guildmaster and Councilor staff ranks. They are assigned by hand
/// and may be edited or deleted like any other role.</para>
/// </summary>
public static class BuiltInRoles
{
	/// <summary>Discord's @everyone: held by every active account, priority 0.</summary>
	public const string EveryoneSlug = "everyone";

	/// <summary>True when <paramref name="role"/> is the <c>everyone</c> role.</summary>
	public static bool IsEveryone(SharpRole role) => role.Slug == EveryoneSlug;

	/// <summary>The built-in role slug for a derived <see cref="PortalRole"/> (e.g. Wizard → "wizard").</summary>
	public static string SlugFor(PortalRole role) => role.ToString().ToLowerInvariant();

	/// <summary>
	/// The tier role slugs an account at <paramref name="tier"/> holds: <c>guest</c> alone for an
	/// account without characters, otherwise <c>player</c> up to and including the tier.
	/// </summary>
	public static IReadOnlyList<string> TierSlugs(PortalRole tier) => tier == PortalRole.Guest
		? [SlugFor(PortalRole.Guest)]
		: Enum.GetValues<PortalRole>().Where(r => r >= PortalRole.Player && r <= tier).Select(SlugFor).ToArray();

	private static readonly string[] PlayerScopes =
	[
		PortalPermission.WikiCreate,
		PortalPermission.WikiEdit,
		PortalPermission.MediaUpload,
		PortalPermission.SoftcodeUse,
		PortalPermission.SnapshotCapture,
		PortalPermission.SnapshotRestore,
		PortalPermission.JobsManageOwn,
		PortalPermission.QueueInspectOwn,
		PortalPermission.QueueControlOwn,
	];

	private static readonly string[] BuilderScopes = [PortalPermission.DiagnosticsProfile];

	private static readonly string[] RoyaltyScopes =
	[
		PortalPermission.PlayersModerate,
		PortalPermission.WikiAdmin,
		PortalPermission.MediaAdmin,
		PortalPermission.QueueInspect,
	];

	private static readonly string[] HelperScopes = [PortalPermission.PlayersView, PortalPermission.QueueInspect];

	private static readonly string[] ModeratorScopes =
	[
		PortalPermission.PlayersModerate,
		PortalPermission.WikiAdmin,
		PortalPermission.MediaAdmin,
		PortalPermission.QueueInspect,
		PortalPermission.QueueControl,
		PortalPermission.RolesAdmin,
	];

	/// <summary>Seed templates for every system role (timestamps stamped at insert time).</summary>
	public static readonly IReadOnlyList<SharpRole> All = BuildSystem();

	/// <summary>Seed templates for the starter roles, written once into a world that has no roles yet.</summary>
	public static readonly IReadOnlyList<SharpRole> Starters =
	[
		Template("helper", "Helper", 12, "#4fc3c8", false, HelperScopes),
		Template("moderator", "Moderator", 25, "#ff9f6b", false, ModeratorScopes),
	];

	private static IReadOnlyList<SharpRole> BuildSystem()
	{
		var roles = new List<SharpRole> { Template(EveryoneSlug, "Everyone", 0, "#7d8790", true, [PortalPermission.WikiRead]) };
		foreach (var role in Enum.GetValues<PortalRole>())
		{
			string[] scopes = role switch
			{
				PortalRole.God => [PortalPermission.Administrator],
				PortalRole.Wizard => PortalPermission.AllScopes
					.Where(s => s is not (PortalPermission.ServerAdmin or PortalPermission.Administrator)).ToArray(),
				PortalRole.Royalty => RoyaltyScopes,
				PortalRole.Builder => BuilderScopes,
				PortalRole.Player => PlayerScopes,
				_ => []
			};
			roles.Add(Template(SlugFor(role), role.ToString(), (int)role, ColorFor(role), true, scopes));
		}

		return roles;
	}

	private static SharpRole Template(string slug, string name, int priority, string color, bool system, string[] scopes) => new()
	{
		Slug = slug,
		Name = name,
		Priority = priority,
		IsSystem = system,
		Color = color,
		Permissions = scopes.ToDictionary(s => s, _ => PermissionState.Allow)
	};

	private static string ColorFor(PortalRole role) => role switch
	{
		PortalRole.God => "#ffd166",
		PortalRole.Wizard => "#5aa9ff",
		PortalRole.Royalty => "#b39cff",
		PortalRole.Builder => "#6cde9a",
		PortalRole.Player => "#9aa3ab",
		_ => "#7d8790"
	};
}
