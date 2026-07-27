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

	public const string WikiRead = "wiki.read";
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

	/// <summary>Every scope, in editor display order, grouped like the nav.</summary>
	public static readonly IReadOnlyList<Definition> All =
	[
		new(WikiRead, "EnumPermWikiRead", GroupContent, "EnumPermWikiReadDesc"),
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
	];

	/// <summary>
	/// Coarse-scope ⇒ implied finer scopes. Applied as a closure when computing the granted set
	/// (see <c>Expand</c>), so a role that grants only <c>wiki.admin</c> still authorizes the wiki
	/// read/create/edit/delete gates. Keep shallow (one level); the expander is not recursive.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string[]> Implications =
		new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
		{
			[WikiAdmin] = [WikiRead, WikiCreate, WikiEdit, WikiDelete],
			[MediaAdmin] = [MediaUpload],
			[PlayersModerate] = [PlayersView],
		};

	/// <summary>Flat list of every scope string, in editor display order.</summary>
	public static readonly IReadOnlyList<string> AllScopes = All.Select(d => d.Scope).ToList();

	private static readonly IReadOnlySet<string> AllScopesSet =
		AllScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="scope"/> is a known permission scope. Case-insensitive,
	/// matching <see cref="Expand"/> and <see cref="Implications"/>.</summary>
	public static bool IsKnown(string scope) => AllScopesSet.Contains(scope);

	/// <summary>
	/// Expands a granted scope set to include every scope implied by a coarser one (e.g.
	/// <c>wiki.admin</c> ⇒ <c>wiki.read/create/edit/delete</c>). Used at token-issue time so the
	/// finer gates authorize for holders of the umbrella scope.
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
