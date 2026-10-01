using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace SharpMUSH.Client.Layout;

/// <summary>
/// The Build &amp; manage destinations and the gate each one sits behind, declared once. The desktop
/// rail (whether to show the section at all), the section's own sidebar and the mobile drawer's
/// Build and Manage groups all read this, so a gate cannot differ between them.
/// </summary>
/// <remarks>
/// A gate is any-of: an entry is visible when the viewer satisfies any one of its policies, or holds
/// any one of its <c>perm</c> claims. That covers the fallback pairs the menu has always had
/// (<c>queue.inspect</c> or <c>queue.inspect.own</c>; <c>jobs.manage.own</c> or <c>jobs.manage</c>)
/// and the snapshots page, which is gated on a claim rather than a policy.
/// </remarks>
public static class BuildNavCatalog
{
	/// <summary>Which group of the section an entry belongs to (the drawer keeps the two headings).</summary>
	public enum Group
	{
		Build,
		Manage,
	}

	/// <summary>One destination.</summary>
	/// <param name="Href">The page.</param>
	/// <param name="Icon">A MudBlazor icon.</param>
	/// <param name="LabelKey">Its <c>SharedResource</c> label.</param>
	/// <param name="Group">Build or Manage.</param>
	/// <param name="Policies">Any one of these policies shows it.</param>
	/// <param name="PermClaims">Or any one of these <c>perm</c> claims.</param>
	/// <param name="ExactMatch">Current only on exactly this path (the dashboard at <c>/admin</c>).</param>
	public sealed record Entry(
		string Href,
		string Icon,
		string LabelKey,
		Group Group,
		IReadOnlyList<string> Policies,
		IReadOnlyList<string>? PermClaims = null,
		bool ExactMatch = false);

	/// <summary>Every entry, in menu order.</summary>
	public static IReadOnlyList<Entry> All { get; } =
	[
		new("/softcode", MudBlazor.Icons.Material.Filled.Code, "SoftcodeEditor", Group.Build, ["softcode.use"]),
		new("/admin/applications", MudBlazor.Icons.Material.Filled.Apps, "AdmApplicationsTitle", Group.Build, ["applications.admin"]),
		new("/admin/layout", MudBlazor.Icons.Material.Filled.Dashboard, "LayLayouts", Group.Build, ["layout.admin"]),
		new("/admin/snapshots", MudBlazor.Icons.Material.Filled.History, "SnapshotsTitle", Group.Build, [], ["snapshots.capture", "snapshots.restore"]),
		new("/admin/packages", MudBlazor.Icons.Material.Filled.Inventory2, "PkgPackages", Group.Build, ["packages.admin"]),
		new("/admin/diagnostics", MudBlazor.Icons.Material.Filled.QueryStats, "DiagTitle", Group.Manage, ["queue.inspect", "queue.inspect.own"]),
		new("/admin", MudBlazor.Icons.Material.Filled.Dashboard, "AdmBreadcrumb", Group.Manage, ["players.view"], ExactMatch: true),
		new("/admin/jobs", MudBlazor.Icons.Material.Filled.Schedule, "JobsTitle", Group.Manage, ["jobs.manage.own", "jobs.manage"]),
		new("/admin/roles", MudBlazor.Icons.Material.Filled.Shield, "AdmRolesTitle", Group.Manage, ["roles.admin"]),
		new("/admin/wiki", MudBlazor.Icons.Material.Filled.AdminPanelSettings, "WikiAdmin", Group.Manage, ["wiki.admin"]),
		new("/admin/media", MudBlazor.Icons.Material.Filled.PermMedia, "AdmMediaTitle", Group.Manage, ["media.admin"]),
		new("/admin/config", MudBlazor.Icons.Material.Filled.Tune, "Config", Group.Manage, ["config.admin"]),
	];

	/// <summary>The entries <paramref name="user"/> may see, in menu order.</summary>
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

		return visible;
	}

	private static async Task<bool> IsVisibleAsync(IAuthorizationService authorization, ClaimsPrincipal user, Entry entry)
	{
		if (entry.PermClaims is { } claims && claims.Any(c => user.HasClaim("perm", c)))
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
