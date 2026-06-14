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

	// ── Content: Wiki (read/create/edit/delete tiers + moderation) ──
	public const string WikiRead = "wiki.read";
	public const string WikiCreate = "wiki.create";
	public const string WikiEdit = "wiki.edit";
	public const string WikiDelete = "wiki.delete";
	public const string WikiAdmin = "wiki.admin";

	// ── Content: Media (contributor upload vs library management) ──
	public const string MediaUpload = "media.upload";
	public const string MediaAdmin = "media.admin";

	// ── Build ──
	public const string ApplicationsAdmin = "applications.admin";
	public const string PackagesAdmin = "packages.admin";

	// ── Manage ──
	public const string ConfigAdmin = "config.admin";
	public const string RolesAdmin = "roles.admin";
	public const string PlayersView = "players.view";
	public const string PlayersModerate = "players.moderate";
	public const string LayoutAdmin = "layout.admin";
	public const string ServerAdmin = "server.admin";

	/// <summary>Display metadata for one scope, used by the role-editor permission matrix.</summary>
	public sealed record Definition(string Scope, string Label, string Group, string Description);

	/// <summary>Every scope, in editor display order, grouped like the nav.</summary>
	public static readonly IReadOnlyList<Definition> All =
	[
		new(WikiRead, "Wiki · Read", "Content", "View unpublished/draft wiki pages (published pages are public to everyone)."),
		new(WikiCreate, "Wiki · Create", "Content", "Create new wiki pages."),
		new(WikiEdit, "Wiki · Edit", "Content", "Edit, rollback, and retag existing (unprotected) wiki pages."),
		new(WikiDelete, "Wiki · Delete", "Content", "Delete wiki pages and their revision history."),
		new(WikiAdmin, "Wiki · Moderate", "Content", "Protect/unprotect, edit protected pages, batch operations, and the wiki admin dashboard."),
		new(MediaUpload, "Image Library · Upload", "Content", "Upload image assets for use in wiki pages."),
		new(MediaAdmin, "Image Library · Manage", "Content", "Browse and delete the shared media library."),
		new(ApplicationsAdmin, "Applications", "Build", "Register and manage schema-driven applications."),
		new(PackagesAdmin, "Packages", "Build", "Install, update, and manage softcode packages."),
		new(ConfigAdmin, "Server Config", "Manage", "Edit server configuration, sitelock, banned names, restrictions."),
		new(RolesAdmin, "Roles & Permissions", "Manage", "Create roles, edit permissions, and assign roles to accounts."),
		new(PlayersView, "Players · View", "Manage", "Browse player accounts, characters, and profiles (read-only)."),
		new(PlayersModerate, "Players · Moderate", "Manage", "Disable, ban, warn, and edit player accounts, characters, and profiles."),
		new(LayoutAdmin, "Layout Editor", "Manage", "Edit the portal widget layout."),
		new(ServerAdmin, "Server (God)", "Manage", "Server-level operations and database import. God-only by default."),
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

	/// <summary>Flat list of every scope string.</summary>
	public static readonly IReadOnlyList<string> AllScopes = All.Select(d => d.Scope).ToList();

	/// <summary>True when <paramref name="scope"/> is a known permission scope.</summary>
	public static bool IsKnown(string scope) => AllScopes.Contains(scope);

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
