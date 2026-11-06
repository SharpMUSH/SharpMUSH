namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The catalog of portal permission scopes (the things a role can be granted). These back the
/// policy-based authorization gates (<c>[Authorize(Policy = PortalPermission.WikiEdit)]</c>) and
/// the role editor's permission matrix. Scope strings are stable identifiers — do not rename.
///
/// Granularity follows the rule "split where trust levels genuinely differ, stay coarse where an
/// area is all-or-nothing": Wiki and Media are split into action tiers; Players into view/moderate;
/// the remaining management areas are single admin scopes. Coarser scopes <em>imply</em> the finer
/// ones below them (see <see cref="Implications"/>), so granting <c>wiki.admin</c> still confers
/// read/create/edit/delete.
/// </summary>
public static class PortalPermission
{
	/// <summary>JWT/claims type carrying one granted permission scope per value.</summary>
	public const string ClaimType = "perm";

	public const string SnapshotCapture = "snapshots.capture";
	public const string SnapshotRestore = "snapshots.restore";
	public const string JobsManageOwn = "jobs.manage.own";
	public const string JobsManage = "jobs.manage";
	public const string QueueInspectOwn = "queue.inspect.own";
	public const string QueueInspect = "queue.inspect";
	public const string QueueControlOwn = "queue.control.own";
	public const string QueueControl = "queue.control";
	public const string DiagnosticsProfile = "diagnostics.profile";
	public const string RealityAdmin = "reality.admin";

	public const string WikiRead = "wiki.read";

	/// <summary>Sees unpublished (draft) wiki pages besides one's own. Reading still needs <see cref="WikiRead"/>.</summary>
	public const string WikiDrafts = "wiki.drafts";
	public const string WikiCreate = "wiki.create";
	public const string WikiEdit = "wiki.edit";
	public const string WikiDelete = "wiki.delete";
	public const string WikiAdmin = "wiki.admin";

	public const string MediaUpload = "media.upload";
	public const string MediaAdmin = "media.admin";

	public const string SoftcodeUse = "softcode.use";
	public const string ApplicationsAdmin = "applications.admin";
	public const string PackagesAdmin = "packages.admin";

	public const string ConfigAdmin = "config.admin";
	public const string RolesAdmin = "roles.admin";
	public const string PlayersView = "players.view";
	public const string PlayersModerate = "players.moderate";
	public const string LayoutAdmin = "layout.admin";
	public const string ServerAdmin = "server.admin";

	/// <summary>
	/// Discord's ADMINISTRATOR: a role that allows it holds every scope, and no per-account Deny
	/// override applies to it. It cannot be granted or denied as a per-account override.
	/// </summary>
	public const string Administrator = "administrator";

	/// <summary>PennMUSH's <c>Wizard()</c>: the WIZARD flag's meaning. The <c>wizard</c> role allows it.</summary>
	public const string GameWizard = "game.wizard";

	/// <summary>PennMUSH's ROYALTY flag. The <c>royalty</c> role allows it.</summary>
	public const string GameRoyalty = "game.royalty";

	/// <summary>
	/// Runs channels and staff messaging: Wizard channels, channel administration, @wizwall and @wall,
	/// the MOTDs and mail administration. Part of what WIZARD meant; the <c>wizard</c> role allows it.
	/// </summary>
	public const string ChatAdmin = "chat.admin";

	/// <summary>
	/// Defines and runs feeds (<c>help @feed</c>): kinds, their taps, members and limits. A kind's owner runs
	/// its own feeds without it. The <c>wizard</c> role allows it.
	/// </summary>
	public const string FeedAdmin = "feed.admin";

	/// <summary>
	/// Sends push notifications to players' devices with <c>@push</c> (<c>help @push</c>). Meant for the object of
	/// a package that decides what notifies, such as the bundled <c>notifications</c> package. The <c>wizard</c>
	/// role allows it.
	/// </summary>
	public const string NotifyPush = "notify.push";

	/// <summary>
	/// Calls side-effect functions (pemit(), set(), create() and the like) when the <c>sidefx_flag</c>
	/// option asks for the SIDEFX flag, as RhostMUSH does. The <c>wizard</c> role allows it.
	/// </summary>
	public const string SideFx = "softcode.sidefx";

	/// <summary>
	/// Operates the running server: @shutdown, @dump, @dbck, @purge, @backup, @storage, @log and the
	/// like. Part of what WIZARD meant; the <c>wizard</c> role allows it. Not <see cref="ServerAdmin"/>,
	/// which is the owner's.
	/// </summary>
	public const string ServerOperate = "server.operate";

	/// <summary>Takes another's object with <c>@chown</c>, and a thing not carried. RhostMUSH's CHOWN_ME and CHOWN_ANYWHERE.</summary>
	public const string ChownSelf = "game.chown.self";

	/// <summary>
	/// Gives anything to anyone with <c>@chown</c>, and <c>@chown/preserve</c>, <c>@chownall</c> and <c>@atrchown</c>
	/// to anyone. Covers <see cref="ChownSelf"/>. RhostMUSH's CHOWN_OTHER. <c>game.wizard</c> covers it.
	/// </summary>
	public const string ChownAny = "game.chown.any";

	/// <summary>Destroys players with <c>@destroy</c> and <c>@nuke</c>. <c>game.wizard</c> covers it.</summary>
	public const string Nuke = "game.nuke";

	/// <summary>
	/// Writes wizard-flagged and locked attributes and locks on what it controls, and sets any lock there.
	/// <c>game.wizard</c> covers it.
	/// </summary>
	public const string Override = "game.override";

	/// <summary>Passes enter, leave and exit locks. RhostMUSH's WRAITH. No role allows it by default.</summary>
	public const string OverrideMovement = "game.override.movement";

	/// <summary>
	/// Sets the flags only wizards may set, and CHOWN_OK and DESTROY_OK on what others own. RhostMUSH's SECURITY.
	/// <c>game.wizard</c> covers it.
	/// </summary>
	public const string FlagsRestricted = "game.flags.restricted";

	/// <summary>
	/// Nobody ranked below the holder may <c>@force</c> it, or, with a reach, nobody ranked at or below the reach.
	/// RhostMUSH's NOFORCE. <c>game.wizard</c> covers it.
	/// </summary>
	public const string NoForce = "game.noforce";

	/// <summary>
	/// Nobody ranked below the holder may <c>@boot</c> it, or, with a reach, nobody ranked at or below the reach.
	/// RhostMUSH's NO_BOOT. No role allows it by default.
	/// </summary>
	public const string NoBoot = "game.noboot";
	/// <summary>Sends a page; checked against each recipient. Allowed unless denied (<see cref="AllowedUnlessDenied"/>).</summary>
	public const string PlayerPage = "player.page";

	/// <summary>Builds: @create, @dig, @open, @clone and their functions. Allowed unless denied.</summary>
	public const string PlayerCreate = "player.create";

	/// <summary>Gives money or things with give. Allowed unless denied.</summary>
	public const string PlayerGive = "player.give";

	/// <summary>Is given money or things; checked on the receiver. Allowed unless denied.</summary>
	public const string PlayerReceive = "player.receive";

	/// <summary>@forces what it controls. Allowed unless denied.</summary>
	public const string PlayerForce = "player.force";

	/// <summary>Uses channels: @channel, @chat, @cemit and channel aliases. Allowed unless denied.</summary>
	public const string PlayerChat = "player.chat";

	/// <summary>Runs <c>$</c>-commands. Allowed unless denied.</summary>
	public const string PlayerCommands = "player.commands";

	/// <summary>Examines others' VISUAL objects. Allowed unless denied.</summary>
	public const string PlayerExamine = "player.examine";

	/// <summary>
	/// Controls others' objects through a zone, a control lock or a shared owner: the control that does
	/// not come from owning the object. Allowed unless denied.
	/// </summary>
	public const string PlayerModify = "player.modify";

	/// <summary>Takes CHOWN_OK objects with @chown. Allowed unless denied.</summary>
	public const string PlayerChown = "player.chown";

	/// <summary>@teleports by control or JUMP_OK. Allowed unless denied.</summary>
	public const string PlayerTeleport = "player.teleport";

	/// <summary>Changes its own password with @password. Allowed unless denied.</summary>
	public const string PlayerPassword = "player.password";

	/// <summary>
	/// The everyday scopes: granted to every object unless a role or override denies them, so nobody notices them until
	/// a deny takes one away. They exist so that a RhostMUSH depower, or staff muting someone, has something precise to deny.
	/// </summary>
	public static readonly IReadOnlySet<string> AllowedUnlessDenied = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		PlayerPage, PlayerCreate, PlayerGive, PlayerReceive, PlayerForce, PlayerChat, PlayerCommands, PlayerExamine,
		PlayerModify, PlayerChown, PlayerTeleport, PlayerPassword,
	};

	/// <summary>Controls every object except the owner's, whoever owns it (the wizard half of PennMUSH <c>controls()</c>).</summary>
	public const string ControlAll = "control.all";

	/// <summary>
	/// Guards a role: what holds a role that allows this is controlled only by holders of that role's
	/// <see cref="ControlRole"/>. Read on each role the target holds, never pooled, so one guarded role is enough.
	/// </summary>
	public const string ProtectRole = "protect.role";

	/// <summary>What <see cref="ControlRole"/> puts before the role's slug.</summary>
	public const string ControlRolePrefix = "control.role.";

	/// <summary>
	/// Lets its holders past the guard of the role <paramref name="slug"/> (<see cref="ProtectRole"/>). Passing a guard
	/// grants no control by itself; ownership, zones and <see cref="ControlAll"/> still decide.
	/// </summary>
	public static string ControlRole(string slug) => ControlRolePrefix + slug.ToLowerInvariant();

	/// <summary>True for a <see cref="ControlRole"/> scope, whatever role it names.</summary>
	public static bool IsControlRole(string scope)
		=> scope.Length > ControlRolePrefix.Length && scope.StartsWith(ControlRolePrefix, StringComparison.OrdinalIgnoreCase);

	/// <summary>The scope backing a PennMUSH power, e.g. <c>game.see_all</c> for See_All.</summary>
	public static string GamePower(string power) => "game." + power.ToLowerInvariant();

	/// <summary>
	/// The scopes whose checks act on a target and read an override's reach (<see cref="OverrideReach"/>). An override
	/// on any other scope is refused a reach, since nothing would honour it.
	/// </summary>
	private static readonly IReadOnlySet<string> ReachScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		ControlAll,
		GamePower("Tport_Anything"),
		GamePower("Tport_Anywhere"),
		ChownSelf,
		ChownAny,
		Nuke,
		Override,
		FlagsRestricted,
		NoForce,
		NoBoot,
		OverrideMovement,
		PlayerPage,
		PlayerGive,
		PlayerExamine,
		PlayerModify,
		PlayerChown,
		PlayerTeleport,
	};

	/// <summary>Whether an override of <paramref name="scope"/> may carry a reach: whether its checks read one.</summary>
	public static bool TakesReach(string scope) => ReachScopes.Contains(scope);

	/// <summary>Every scope that takes a reach, in catalog order.</summary>
	public static IEnumerable<string> ReachableScopes => AllScopes.Where(TakesReach);

	/// <summary>True for the in-game scopes: <c>game.*</c>, <see cref="ControlAll"/>, <see cref="ProtectRole"/> and each <see cref="ControlRole"/>.</summary>
	public static bool IsGameScope(string scope) => GameScopes.Contains(scope) || IsControlRole(scope);

	/// <summary>
	/// Display metadata for one scope, used by the role-editor permission matrix. Everything but
	/// <paramref name="Scope"/> is a <c>SharedResource</c> key rather than text — a static list cannot
	/// reach the render site's localizer, so the matrix resolves these through <c>Loc[...]</c>.
	/// </summary>
	/// <param name="Scope">The stable permission scope string. API surface — never localize or rename.</param>
	/// <param name="LabelKey">Resource key for the row's short label.</param>
	/// <param name="GroupKey">Resource key for the section heading this row sits under. Doubles as the grouping key.</param>
	/// <param name="DescriptionKey">Resource key for the sentence explaining what granting the scope allows.</param>
	public sealed record Definition(string Scope, string LabelKey, string GroupKey, string DescriptionKey);

	internal const string GroupContent = "Content";
	internal const string GroupBuild = "EnumPermGroupBuild";
	internal const string GroupManage = "EnumPermGroupManage";
	internal const string GroupGame = "EnumPermGroupGame";

	/// <summary>Every scope, in editor display order, grouped like the nav.</summary>
	public static readonly IReadOnlyList<Definition> All =
	[
		new(SnapshotCapture, "EnumPermSnapshotCapture", GroupManage, "EnumPermSnapshotCaptureDesc"),
		new(SnapshotRestore, "EnumPermSnapshotRestore", GroupManage, "EnumPermSnapshotRestoreDesc"),
		new(JobsManageOwn, "EnumPermJobsManageOwn", GroupManage, "EnumPermJobsManageOwnDesc"),
		new(JobsManage, "EnumPermJobsManage", GroupManage, "EnumPermJobsManageDesc"),
		new(QueueInspectOwn, "EnumPermQueueInspectOwn", GroupManage, "EnumPermQueueInspectOwnDesc"),
		new(QueueInspect, "EnumPermQueueInspect", GroupManage, "EnumPermQueueInspectDesc"),
		new(QueueControlOwn, "EnumPermQueueControlOwn", GroupManage, "EnumPermQueueControlOwnDesc"),
		new(QueueControl, "EnumPermQueueControl", GroupManage, "EnumPermQueueControlDesc"),
		new(DiagnosticsProfile, "EnumPermDiagnosticsProfile", GroupManage, "EnumPermDiagnosticsProfileDesc"),
		new(RealityAdmin, "EnumPermRealityAdmin", GroupManage, "EnumPermRealityAdminDesc"),
		new(WikiRead, "EnumPermWikiRead", GroupContent, "EnumPermWikiReadDesc"),
		new(WikiDrafts, "EnumPermWikiDrafts", GroupContent, "EnumPermWikiDraftsDesc"),
		new(WikiCreate, "EnumPermWikiCreate", GroupContent, "EnumPermWikiCreateDesc"),
		new(WikiEdit, "EnumPermWikiEdit", GroupContent, "EnumPermWikiEditDesc"),
		new(WikiDelete, "EnumPermWikiDelete", GroupContent, "EnumPermWikiDeleteDesc"),
		new(WikiAdmin, "EnumPermWikiAdmin", GroupContent, "EnumPermWikiAdminDesc"),
		new(MediaUpload, "EnumPermMediaUpload", GroupContent, "EnumPermMediaUploadDesc"),
		new(MediaAdmin, "EnumPermMediaAdmin", GroupContent, "EnumPermMediaAdminDesc"),
		new(SoftcodeUse, "EnumPermSoftcodeUse", GroupBuild, "EnumPermSoftcodeUseDesc"),
		new(ApplicationsAdmin, "EnumPermApplicationsAdmin", GroupBuild, "EnumPermApplicationsAdminDesc"),
		new(PackagesAdmin, "EnumPermPackagesAdmin", GroupBuild, "EnumPermPackagesAdminDesc"),
		new(ConfigAdmin, "EnumPermConfigAdmin", GroupManage, "EnumPermConfigAdminDesc"),
		new(RolesAdmin, "EnumPermRolesAdmin", GroupManage, "EnumPermRolesAdminDesc"),
		new(PlayersView, "EnumPermPlayersView", GroupManage, "EnumPermPlayersViewDesc"),
		new(PlayersModerate, "EnumPermPlayersModerate", GroupManage, "EnumPermPlayersModerateDesc"),
		new(LayoutAdmin, "EnumPermLayoutAdmin", GroupManage, "EnumPermLayoutAdminDesc"),
		new(ServerAdmin, "EnumPermServerAdmin", GroupManage, "EnumPermServerAdminDesc"),
		new(Administrator, "EnumPermAdministrator", GroupManage, "EnumPermAdministratorDesc"),
		new(GameWizard, "EnumPermGameWizard", GroupGame, "EnumPermGameWizardDesc"),
		new(GameRoyalty, "EnumPermGameRoyalty", GroupGame, "EnumPermGameRoyaltyDesc"),
		new(ChatAdmin, "EnumPermChatAdmin", GroupGame, "EnumPermChatAdminDesc"),
		new(FeedAdmin, "EnumPermFeedAdmin", GroupGame, "EnumPermFeedAdminDesc"),
		new(NotifyPush, "EnumPermNotifyPush", GroupGame, "EnumPermNotifyPushDesc"),
		new(SideFx, "EnumPermSideFx", GroupGame, "EnumPermSideFxDesc"),
		new(ServerOperate, "EnumPermServerOperate", GroupGame, "EnumPermServerOperateDesc"),
		new(ControlAll, "EnumPermControlAll", GroupGame, "EnumPermControlAllDesc"),
		new(ProtectRole, "EnumPermProtectRole", GroupGame, "EnumPermProtectRoleDesc"),
		new(ChownSelf, "EnumPermChownSelf", GroupGame, "EnumPermChownSelfDesc"),
		new(ChownAny, "EnumPermChownAny", GroupGame, "EnumPermChownAnyDesc"),
		new(Nuke, "EnumPermNuke", GroupGame, "EnumPermNukeDesc"),
		new(Override, "EnumPermOverride", GroupGame, "EnumPermOverrideDesc"),
		new(OverrideMovement, "EnumPermOverrideMovement", GroupGame, "EnumPermOverrideMovementDesc"),
		new(FlagsRestricted, "EnumPermFlagsRestricted", GroupGame, "EnumPermFlagsRestrictedDesc"),
		new(NoForce, "EnumPermNoForce", GroupGame, "EnumPermNoForceDesc"),
		new(NoBoot, "EnumPermNoBoot", GroupGame, "EnumPermNoBootDesc"),
		new(PlayerPage, "EnumPermPlayerPage", GroupGame, "EnumPermPlayerPageDesc"),
		new(PlayerCreate, "EnumPermPlayerCreate", GroupGame, "EnumPermPlayerCreateDesc"),
		new(PlayerGive, "EnumPermPlayerGive", GroupGame, "EnumPermPlayerGiveDesc"),
		new(PlayerReceive, "EnumPermPlayerReceive", GroupGame, "EnumPermPlayerReceiveDesc"),
		new(PlayerForce, "EnumPermPlayerForce", GroupGame, "EnumPermPlayerForceDesc"),
		new(PlayerChat, "EnumPermPlayerChat", GroupGame, "EnumPermPlayerChatDesc"),
		new(PlayerCommands, "EnumPermPlayerCommands", GroupGame, "EnumPermPlayerCommandsDesc"),
		new(PlayerExamine, "EnumPermPlayerExamine", GroupGame, "EnumPermPlayerExamineDesc"),
		new(PlayerModify, "EnumPermPlayerModify", GroupGame, "EnumPermPlayerModifyDesc"),
		new(PlayerChown, "EnumPermPlayerChown", GroupGame, "EnumPermPlayerChownDesc"),
		new(PlayerTeleport, "EnumPermPlayerTeleport", GroupGame, "EnumPermPlayerTeleportDesc"),
		new(PlayerPassword, "EnumPermPlayerPassword", GroupGame, "EnumPermPlayerPasswordDesc"),
		.. GamePowers.All.Select(power => new Definition(power.Scope, power.ResourceKey, GroupGame, power.ResourceKey + "Desc")),
	];

	private static readonly IReadOnlySet<string> GameScopes = All
		.Where(d => d.GroupKey == GroupGame).Select(d => d.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Coarse-scope ⇒ implied finer scopes. Applied as a closure when computing the granted set
	/// (see <c>Expand</c>), so a role that grants only <c>wiki.admin</c> still authorizes the wiki
	/// read/create/edit/delete gates. Keep shallow (one level); the expander is not recursive.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string[]> Implications =
		new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
		{
			[JobsManage] = [JobsManageOwn],
			[QueueInspect] = [QueueInspectOwn],
			[QueueControl] = [QueueControlOwn],
			[WikiAdmin] = [WikiRead, WikiDrafts, WikiCreate, WikiEdit, WikiDelete],
			[MediaAdmin] = [MediaUpload],
			[PlayersModerate] = [PlayersView],
			[ChownAny] = [ChownSelf],
			[GameWizard] = [ChownAny, Nuke, Override, FlagsRestricted, NoForce],
		};

	/// <summary>The umbrella scopes that directly imply <paramref name="scope"/>.</summary>
	public static IEnumerable<string> ParentScopes(string scope) => ParentsOf(scope);

	/// <inheritdoc cref="ParentScopes"/>
	internal static string[] ParentsOf(string scope) => Parents.GetValueOrDefault(scope, []);

	/// <summary><see cref="Implications"/> the other way round: each implied scope and the umbrellas that imply it.</summary>
	private static readonly IReadOnlyDictionary<string, string[]> Parents = Implications
		.SelectMany(pair => pair.Value.Select(child => (Child: child, Parent: pair.Key)))
		.GroupBy(edge => edge.Child, StringComparer.OrdinalIgnoreCase)
		.ToDictionary(group => group.Key, group => group.Select(edge => edge.Parent).ToArray(), StringComparer.OrdinalIgnoreCase);

	/// <summary>The catalog spelling of <paramref name="scope"/>, or null when it is not a known scope.</summary>
	public static string? Canonical(string scope) =>
		IsControlRole(scope)
			? scope.ToLowerInvariant()
			: AllScopes.FirstOrDefault(known => string.Equals(known, scope, StringComparison.OrdinalIgnoreCase));

	/// <summary>Scopes directly implied by an umbrella grant.</summary>
	public static IReadOnlyList<string> ImpliedScopes(string scope) =>
		Implications.TryGetValue(scope, out var children) ? children : [];

	/// <summary>Flat list of every scope string, in editor display order.</summary>
	public static readonly IReadOnlyList<string> AllScopes = All.Select(d => d.Scope).ToList();

	private static readonly IReadOnlySet<string> AllScopesSet =
		AllScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="scope"/> is a known permission scope, a <see cref="ControlRole"/> for any
	/// role included. Case-insensitive, matching <see cref="Expand"/> and <see cref="Implications"/>.</summary>
	public static bool IsKnown(string scope) => AllScopesSet.Contains(scope) || IsControlRole(scope);

	private static readonly IReadOnlyDictionary<string, string> GroupsByScope =
		All.ToDictionary(d => d.Scope, d => d.GroupKey, StringComparer.OrdinalIgnoreCase);

	/// <summary>The group a built-in scope is listed under in the role editor, or null when it is not one.</summary>
	public static string? GroupOf(string scope) => IsControlRole(scope) ? GroupGame : GroupsByScope.GetValueOrDefault(scope);

	/// <summary>
	/// Expands a granted scope set to include every scope implied by a coarser one (e.g.
	/// <c>wiki.admin</c> ⇒ <c>wiki.read/drafts/create/edit/delete</c>). Only for catalog-only expansion without role restrictions. Authorization must use
	/// PermissionResolver so an explicit child Deny cannot be restored by expansion.
	/// </summary>
	public static IReadOnlySet<string> Expand(IEnumerable<string> scopes)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var scope in scopes)
		{
			result.Add(scope);
			if (Implications.TryGetValue(scope, out var implied))
			{
				foreach (var child in implied)
					result.Add(child);
			}
		}

		return result;
	}
}
