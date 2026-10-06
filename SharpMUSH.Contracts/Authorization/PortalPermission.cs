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
	/// Operates the running server: @shutdown, @dump, @dbck, @purge, @backup, @storage, @log and the
	/// like. Part of what WIZARD meant; the <c>wizard</c> role allows it. Not <see cref="ServerAdmin"/>,
	/// which is the owner's.
	/// </summary>
	public const string ServerOperate = "server.operate";

	/// <summary>Controls every object except the owner's, whoever owns it (the wizard half of PennMUSH <c>controls()</c>).</summary>
	public const string ControlAll = "control.all";

	/// <summary>An object holding this is controlled only by holders of <see cref="ControlAll"/>.</summary>
	public const string ProtectWizard = "protect.wizard";

	/// <summary>An object holding this is not controlled by anyone who lacks it.</summary>
	public const string ProtectAdmin = "protect.admin";

	/// <summary>The scope backing a PennMUSH power, e.g. <c>game.see_all</c> for See_All.</summary>
	public static string GamePower(string power) => "game." + power.ToLowerInvariant();

	/// <summary>True for the in-game scopes: <c>game.*</c>, <see cref="ControlAll"/> and the protections.</summary>
	public static bool IsGameScope(string scope) => GameScopes.Contains(scope);

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

	private const string GroupContent = "Content";
	private const string GroupBuild = "EnumPermGroupBuild";
	private const string GroupManage = "EnumPermGroupManage";
	private const string GroupGame = "EnumPermGroupGame";

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
		new(ServerOperate, "EnumPermServerOperate", GroupGame, "EnumPermServerOperateDesc"),
		new(ControlAll, "EnumPermControlAll", GroupGame, "EnumPermControlAllDesc"),
		new(ProtectWizard, "EnumPermProtectWizard", GroupGame, "EnumPermProtectWizardDesc"),
		new(ProtectAdmin, "EnumPermProtectAdmin", GroupGame, "EnumPermProtectAdminDesc"),
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
		};

	/// <summary>The umbrella scopes that directly imply <paramref name="scope"/>.</summary>
	public static IEnumerable<string> ParentScopes(string scope) =>
		Implications.Where(pair => pair.Value.Contains(scope, StringComparer.OrdinalIgnoreCase)).Select(pair => pair.Key);

	/// <summary>The catalog spelling of <paramref name="scope"/>, or null when it is not a known scope.</summary>
	public static string? Canonical(string scope) =>
		AllScopes.FirstOrDefault(known => string.Equals(known, scope, StringComparison.OrdinalIgnoreCase));

	/// <summary>Scopes directly implied by an umbrella grant.</summary>
	public static IReadOnlyList<string> ImpliedScopes(string scope) =>
		Implications.TryGetValue(scope, out var children) ? children : [];

	/// <summary>Flat list of every scope string, in editor display order.</summary>
	public static readonly IReadOnlyList<string> AllScopes = All.Select(d => d.Scope).ToList();

	private static readonly IReadOnlySet<string> AllScopesSet =
		AllScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="scope"/> is a known permission scope. Case-insensitive,
	/// matching <see cref="Expand"/> and <see cref="Implications"/>.</summary>
	public static bool IsKnown(string scope) => AllScopesSet.Contains(scope);

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
