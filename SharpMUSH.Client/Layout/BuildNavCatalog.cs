using Microsoft.AspNetCore.Authorization;
using MudBlazor;
using System.Security.Claims;

namespace SharpMUSH.Client.Layout;

/// <summary>
/// Every Build &amp; manage destination, its group and the gate it sits behind, declared once. The
/// desktop rail, the section's sidebar, the mobile drawer, the command palette and the overview at
/// <c>/admin</c> all read this, so no two of them can list different pages or gate one differently.
/// </summary>
/// <remarks>
/// A gate is any-of: an entry is visible when the viewer satisfies any one of its policies, holds any
/// one of its <c>perm</c> claims, or is in any one of its roles. That covers the fallback pairs
/// (<c>queue.inspect</c> or <c>queue.inspect.own</c>; <c>jobs.manage.own</c> or <c>jobs.manage</c>),
/// the snapshots page, which is gated on a claim, and accounts, which the server lists for wizards.
/// The overview has no gate of its own: it is shown to whoever may see anything outside Build.
/// </remarks>
public static class BuildNavCatalog
{
	/// <summary>
	/// What an entry is for. Build is a builder's own tools; the rest are staff areas, in the order
	/// the sidebar, the drawer and the overview present them.
	/// </summary>
	public enum Group
	{
		Overview,
		Build,
		People,
		Moderation,
		Content,
		Portal,
		Server,
	}

	/// <summary>One destination.</summary>
	/// <param name="Href">The page.</param>
	/// <param name="Icon">A MudBlazor icon.</param>
	/// <param name="LabelKey">Its <c>SharedResource</c> label.</param>
	/// <param name="DescriptionKey">What a staff member does there, for the overview's card.</param>
	/// <param name="Group">Which group it is listed under.</param>
	/// <param name="Policies">Any one of these policies shows it.</param>
	/// <param name="PermClaims">Or any one of these <c>perm</c> claims.</param>
	/// <param name="Roles">Or any one of these roles.</param>
	/// <param name="ExactMatch">Current only on exactly this path (the overview at <c>/admin</c>).</param>
	public sealed record Entry(
		string Href,
		string Icon,
		string LabelKey,
		string DescriptionKey,
		Group Group,
		IReadOnlyList<string> Policies,
		IReadOnlyList<string>? PermClaims = null,
		IReadOnlyList<string>? Roles = null,
		bool ExactMatch = false);

	/// <summary>
	/// The policy the overview page sits behind: the viewer may open some staff page outside Build.
	/// Registered in <c>Program</c> from <see cref="MayOpenOverview"/>, so typing <c>/admin</c> refuses
	/// a player exactly when no link would have taken them there.
	/// </summary>
	public const string OverviewPolicy = "build.overview";

	/// <summary>
	/// Whether <paramref name="user"/> may see some entry outside Build. Read from claims, the way the
	/// portal's permission policies are (<c>PermissionAuthorizationHandler</c>), so it can back a policy.
	/// </summary>
	public static bool MayOpenOverview(ClaimsPrincipal user) =>
		All.Where(e => e.Group != Group.Build).Any(e =>
			e.Policies.Any(p => user.HasClaim(SharpMUSH.Library.Authorization.PortalPermission.ClaimType, p))
			|| e.PermClaims?.Any(c => user.HasClaim(SharpMUSH.Library.Authorization.PortalPermission.ClaimType, c)) == true
			|| e.Roles?.Any(user.IsInRole) == true);

	/// <summary>The overview: every other staff page as a card, grouped as the sidebar groups them.</summary>
	public static Entry Overview { get; } =
		new("/admin", Icons.Material.Filled.SpaceDashboard, "AdmOverview", "AdmOverviewDescription", Group.Overview, [], ExactMatch: true);

	/// <summary>Every entry but the overview, in menu order.</summary>
	public static IReadOnlyList<Entry> All { get; } =
	[
		new("/softcode", Icons.Material.Filled.Code, "SoftcodeEditor", "AdmCardSoftcode", Group.Build, ["softcode.use"]),
		new("/admin/snapshots", Icons.Material.Filled.History, "SnapshotsTitle", "AdmCardSnapshots", Group.Build, [], ["snapshots.capture", "snapshots.restore"]),
		new("/admin/jobs", Icons.Material.Filled.Schedule, "JobsTitle", "AdmCardJobs", Group.Build, ["jobs.manage.own", "jobs.manage"]),
		new("/admin/diagnostics", Icons.Material.Filled.QueryStats, "DiagTitle", "AdmCardDiagnostics", Group.Build, ["queue.inspect", "queue.inspect.own"]),

		new("/admin/accounts", Icons.Material.Filled.ManageAccounts, "AdmAccountsTitle", "AdmCardAccounts", Group.People, [], Roles: ["Wizard", "God"]),
		new("/admin/characters", Icons.Material.Filled.Person, "Characters", "AdmCardCharacters", Group.People, ["players.view"]),
		new("/admin/guests", Icons.Material.Filled.Group, "AdmGuestsTitle", "AdmCardGuests", Group.People, ["players.view"]),
		new("/admin/roles", Icons.Material.Filled.Shield, "AdmRolesLabel", "AdmCardRoles", Group.People, ["roles.admin"]),

		new("/admin/moderation", Icons.Material.Filled.Gavel, "AdmModerationTitle", "AdmModerationCardAction", Group.Moderation, ["players.moderate"]),
		new("/admin/audit", Icons.Material.Filled.ManageHistory, "AdmAuditTitle", "AdmAuditCardAction", Group.Moderation, ["players.moderate"]),

		new("/admin/wiki", Icons.Material.Filled.MenuBook, "AdmWikiLabel", "AdmCardWiki", Group.Content, ["wiki.admin"]),
		new("/admin/media", Icons.Material.Filled.PermMedia, "AdmMediaTitle", "AdmCardMedia", Group.Content, ["media.admin"]),
		new("/admin/suggestions", Icons.Material.Filled.Spellcheck, "AdmSuggestionsLabel", "AdmCardSuggestions", Group.Content, ["config.admin"]),

		new("/admin/applications", Icons.Material.Filled.Apps, "AdmApplicationsTitle", "AdmCardApplications", Group.Portal, ["applications.admin"]),
		new("/admin/layout", Icons.Material.Filled.Dashboard, "AdmLayoutTitle", "AdmCardLayout", Group.Portal, ["layout.admin"]),
		new("/admin/packages", Icons.Material.Filled.Inventory2, "PkgPackages", "AdmCardPackages", Group.Portal, ["packages.admin"]),
		new("/admin/profiles", Icons.Material.Filled.Badge, "ProfileHandler", "AdmCardProfiles", Group.Portal, ["players.moderate"]),

		new("/admin/server", Icons.Material.Filled.Dns, "AdmServerStatusLabel", "AdmCardServer", Group.Server, ["server.admin"]),
		new("/admin/config", Icons.Material.Filled.Tune, "Configuration", "AdmCardConfig", Group.Server, ["config.admin"]),
		new("/admin/import", Icons.Material.Filled.CloudUpload, "AdmImportLabel", "AdmCardImport", Group.Server, ["server.admin"]),
	];

	/// <summary>The <c>SharedResource</c> heading of a group; the overview has none.</summary>
	public static string? GroupLabelKey(Group group) => group switch
	{
		Group.Build => "NavSectionBuild",
		Group.People => "AdmGroupPeople",
		Group.Moderation => "AdmGroupModeration",
		Group.Content => "AdmGroupContent",
		Group.Portal => "AdmGroupPortal",
		Group.Server => "AdmGroupServer",
		_ => null,
	};

	/// <summary>
	/// The entries <paramref name="user"/> may see, in menu order: the overview first when anything
	/// outside Build is visible, since that is where the rail's link and the configuration's way back go.
	/// </summary>
	public static async Task<IReadOnlyList<Entry>> VisibleAsync(IAuthorizationService authorization, ClaimsPrincipal user)
	{
		var visible = new List<Entry>();
		foreach (var entry in All)
		{
			if (await IsVisibleAsync(authorization, user, entry))
			{
				visible.Add(entry);
			}
		}

		return visible.Any(e => e.Group != Group.Build) ? [Overview, .. visible] : visible;
	}

	private static async Task<bool> IsVisibleAsync(IAuthorizationService authorization, ClaimsPrincipal user, Entry entry)
	{
		if (entry.PermClaims is { } claims && claims.Any(c => user.HasClaim("perm", c)))
		{
			return true;
		}

		if (entry.Roles is { } roles && roles.Any(user.IsInRole))
		{
			return true;
		}

		foreach (var policy in entry.Policies)
		{
			if ((await authorization.AuthorizeAsync(user, null, policy)).Succeeded)
			{
				return true;
			}
		}

		return false;
	}
}
