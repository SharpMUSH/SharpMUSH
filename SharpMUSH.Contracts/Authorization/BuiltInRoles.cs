using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The roles a fresh world starts with.
///
/// <para><b>System roles</b> (<see cref="All"/>) cannot be deleted, re-slugged or re-prioritised. Three
/// are implicit and never assigned by hand: <c>everyone</c> (Discord's @everyone) is held by every
/// holder, <c>player</c> by every player character that is not a guest, and <c>god</c> by player #1.
/// PennMUSH's privileges are roles too: <c>wizard</c> is the WIZARD flag, <c>royalty</c> the ROYALTY
/// flag, <c>builder</c> the Builder power and <c>guest</c> the Guest power, so <c>@set</c> and
/// <c>@power</c> assign them and so can <c>@role</c>. <c>approved</c> marks a character that has met the
/// game's own bar for full participation, what <c>isapproved()</c> reads; it is assigned with
/// <c>@role</c>. Their name, colour and permissions stay editable.</para>
///
/// <para><b>Starter roles</b> (<see cref="Starters"/>) are ordinary staff roles seeded once into a new
/// world. They are assigned by hand and may be edited or deleted like any other role.</para>
/// </summary>
public static class BuiltInRoles
{
	/// <summary>Discord's @everyone: held by every holder, priority 0.</summary>
	public const string EveryoneSlug = "everyone";

	public const string GuestSlug = "guest";
	public const string PlayerSlug = "player";
	public const string BuilderSlug = "builder";
	public const string RoyaltySlug = "royalty";
	public const string WizardSlug = "wizard";
	public const string GodSlug = "god";

	/// <summary>A character the game has approved for full participation: what <c>isapproved()</c> reads.</summary>
	public const string ApprovedSlug = "approved";

	/// <summary>
	/// The approved role's priority: just above <c>player</c> and below the starter staff roles, so a
	/// moderator may assign it, and so may a helper given <c>roles.admin</c>.
	/// </summary>
	public const int ApprovedPriority = (int)PortalRole.Player + 1;

	/// <summary>True when <paramref name="role"/> is the <c>everyone</c> role.</summary>
	public static bool IsEveryone(SharpRole role) => role.Slug == EveryoneSlug;

	/// <summary>True for the roles held by being something rather than by assignment: everyone, player and god.</summary>
	public static bool IsImplicit(string slug) => slug is EveryoneSlug or PlayerSlug or GodSlug;

	/// <summary>The built-in role slug for a <see cref="PortalRole"/> (e.g. Wizard → "wizard").</summary>
	public static string SlugFor(PortalRole role) => role.ToString().ToLowerInvariant();

	/// <summary>
	/// The portal tier a context reads as, for the coarse role claim: God for the owner, otherwise the
	/// highest tier role held, otherwise Guest.
	/// </summary>
	public static PortalRole TierOf(PermissionContext context)
		=> context.IsOwner
			? PortalRole.God
			: Enum.GetValues<PortalRole>()
				.Where(tier => context.Roles.Any(role => string.Equals(role.Slug, SlugFor(tier), StringComparison.OrdinalIgnoreCase)))
				.Append(PortalRole.Guest)
				.Max();

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

	private static readonly string[] GuestScopes = [PortalPermission.GamePower("Guest")];

	private static readonly string[] BuilderScopes = [PortalPermission.DiagnosticsProfile, PortalPermission.GamePower("Builder")];

	/// <summary>
	/// PennMUSH royalty sees everything and changes little, so it views players rather than moderating
	/// them: <see cref="PortalPermission.PlayersModerate"/> also covers @newpassword, @boot and @sitelock.
	/// </summary>
	private static readonly string[] RoyaltyScopes =
	[
		PortalPermission.PlayersView,
		PortalPermission.WikiAdmin,
		PortalPermission.MediaAdmin,
		PortalPermission.QueueInspect,
		PortalPermission.GameRoyalty,
		PortalPermission.ProtectAdmin,
	];

	/// <summary>
	/// Every portal scope but server.admin and administrator, and the wizard's in-game standing. Not the
	/// power scopes or game.royalty: a PennMUSH wizard holds no powers and no ROYALTY flag, and softcode
	/// asking <c>haspower()</c> or <c>hasflag()</c> of a wizard expects that answer.
	/// </summary>
	private static readonly string[] WizardScopes =
	[
		.. PortalPermission.AllScopes.Where(s => !PortalPermission.IsGameScope(s)
			&& s is not (PortalPermission.ServerAdmin or PortalPermission.Administrator)),
		PortalPermission.GameWizard,
		PortalPermission.ChatAdmin,
		PortalPermission.ServerOperate,
		PortalPermission.ControlAll,
		PortalPermission.ProtectWizard,
		PortalPermission.ProtectAdmin,
	];

	/// <summary>An approved character may show pictures in what it writes: a pose, a description, a page.</summary>
	private static readonly string[] ApprovedScopes = [PortalPermission.GamePower("Send_Image")];

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
		Template("helper", "Helper", 12, "#4fc3c8", false, HelperScopes, Categories.Staff),
		Template("moderator", "Moderator", 25, "#ff9f6b", false, ModeratorScopes, Categories.Staff),
	];

	/// <summary>
	/// The roles to write so a world has its defaults, given the roles it already has: every missing
	/// system role; the starter roles too when the world has no roles at all, so a deleted starter stays
	/// deleted; and, on a system role that already exists, each in-game scope its defaults allow and the
	/// stored role leaves on Inherit, and its category when it has none. Those scopes are what the WIZARD and ROYALTY flags and the Guest
	/// and Builder powers mean, so a world seeded before they existed gains them; any other edit an
	/// administrator made is kept.
	/// </summary>
	public static IReadOnlyList<SharpRole> SeedChanges(IReadOnlyCollection<SharpRole> existing, long now)
	{
		var stored = existing.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);
		var changes = new List<SharpRole>();
		foreach (var template in stored.Count == 0 ? All.Concat(Starters) : All)
		{
			if (!stored.TryGetValue(template.Slug, out var role))
			{
				changes.Add(Stamp(template, new Dictionary<string, PermissionState>(template.Permissions), now, now));
				continue;
			}

			var missing = template.Permissions
				.Where(p => PortalPermission.IsGameScope(p.Key) && PermissionResolver.StateOf(role.Permissions, p.Key) == PermissionState.Inherit)
				.ToArray();
			if (!role.IsSystem || (missing.Length == 0 && role.Category.Length > 0)) continue;
			var permissions = new Dictionary<string, PermissionState>(role.Permissions);
			foreach (var (scope, state) in missing) permissions[scope] = state;
			changes.Add(Stamp(role, permissions, role.CreatedAt, now, template));
		}

		return changes;
	}

	private static SharpRole Stamp(SharpRole role, Dictionary<string, PermissionState> permissions, long created, long updated,
		SharpRole? template = null) => new()
		{
			Id = role.Id,
			Slug = role.Slug,
			Name = role.Name,
			Category = role.Category.Length > 0 ? role.Category : template?.Category ?? Categories.System,
			Color = role.Color,
			Priority = role.Priority,
			IsSystem = role.IsSystem,
			Permissions = permissions,
			CreatedAt = created,
			UpdatedAt = updated
		};

	private static IReadOnlyList<SharpRole> BuildSystem()
	{
		var roles = new List<SharpRole> { Template(EveryoneSlug, "Everyone", 0, "#7d8790", true, [PortalPermission.WikiRead]) };
		foreach (var role in Enum.GetValues<PortalRole>())
		{
			string[] scopes = role switch
			{
				PortalRole.God => [PortalPermission.Administrator],
				PortalRole.Wizard => WizardScopes,
				PortalRole.Royalty => RoyaltyScopes,
				PortalRole.Builder => BuilderScopes,
				PortalRole.Player => PlayerScopes,
				_ => GuestScopes
			};
			roles.Add(Template(SlugFor(role), role.ToString(), (int)role, ColorFor(role), true, scopes));
		}

		roles.Add(Template(ApprovedSlug, "Approved", ApprovedPriority, "#8bc34a", true, ApprovedScopes));

		return roles;
	}

	private static SharpRole Template(string slug, string name, int priority, string color, bool system, string[] scopes,
		string category = Categories.System) => new()
		{
			Slug = slug,
			Name = name,
			Category = category,
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
