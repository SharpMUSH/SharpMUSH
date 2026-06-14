namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The catalog of portal permission scopes (the things a role can be granted). These back the
/// policy-based authorization gates (<c>[Authorize(Policy = PortalPermission.WikiAdmin)]</c>) and
/// the role editor's permission matrix. Scope strings are stable identifiers — do not rename.
/// </summary>
public static class PortalPermission
{
	/// <summary>JWT/claims type carrying one granted permission scope per value.</summary>
	public const string ClaimType = "perm";

	// ── Content ──
	public const string WikiAdmin = "wiki.admin";
	public const string MediaAdmin = "media.admin";

	// ── Build ──
	public const string ApplicationsAdmin = "applications.admin";
	public const string PackagesAdmin = "packages.admin";

	// ── Manage ──
	public const string ConfigAdmin = "config.admin";
	public const string RolesAdmin = "roles.admin";
	public const string PlayersAdmin = "players.admin";
	public const string LayoutAdmin = "layout.admin";
	public const string ServerAdmin = "server.admin";

	/// <summary>Display metadata for one scope, used by the role-editor permission matrix.</summary>
	public sealed record Definition(string Scope, string Label, string Group, string Description);

	/// <summary>Every scope, in editor display order, grouped like the nav.</summary>
	public static readonly IReadOnlyList<Definition> All =
	[
		new(WikiAdmin, "Wiki", "Content", "Create, edit, protect, and delete wiki pages."),
		new(MediaAdmin, "Image Library", "Content", "Manage uploaded media assets."),
		new(ApplicationsAdmin, "Applications", "Build", "Register and manage schema-driven applications."),
		new(PackagesAdmin, "Packages", "Build", "Install, update, and manage softcode packages."),
		new(ConfigAdmin, "Server Config", "Manage", "Edit server configuration, sitelock, banned names, restrictions."),
		new(RolesAdmin, "Roles & Permissions", "Manage", "Create roles, edit permissions, and assign roles to accounts."),
		new(PlayersAdmin, "Players & Characters", "Manage", "Administer player accounts, characters, profiles, and moderation."),
		new(LayoutAdmin, "Layout Editor", "Manage", "Edit the portal widget layout."),
		new(ServerAdmin, "Server (God)", "Manage", "Server-level operations and database import. God-only by default."),
	];

	/// <summary>Flat list of every scope string.</summary>
	public static readonly IReadOnlyList<string> AllScopes = All.Select(d => d.Scope).ToList();

	/// <summary>True when <paramref name="scope"/> is a known permission scope.</summary>
	public static bool IsKnown(string scope) => AllScopes.Contains(scope);
}
