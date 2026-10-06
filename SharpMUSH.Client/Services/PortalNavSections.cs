using System.Security.Claims;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Pure grouping/ordering logic for data-driven NavBar sections. A registered Page application's
/// <see cref="PortalApplication.NavPlacement"/> names the sidebar <b>section</b> its link belongs to.
/// Built-in section names (<c>Play</c>/<c>World</c>/<c>Build</c>/<c>Manage</c>) slot into the existing
/// hardcoded groups; any other name becomes its own data-driven group.
///
/// <para>This helper is UI-free so it can be unit-tested directly: it filters the accessible Page apps for a
/// caller's <see cref="PortalRole"/>, returns the apps for a given section in <c>Order</c>-then-name order,
/// and returns the novel (non-built-in) sections ordered by their minimum <c>Order</c> (ties broken by
/// section name).</para>
/// </summary>
public static class PortalNavSections
{
	/// <summary>The four sidebar sections that exist as hardcoded groups in <c>NavMenu.razor</c>.</summary>
	public static readonly IReadOnlyList<string> BuiltInSections = ["Play", "World", "Build", "Manage"];

	/// <summary>
	/// The accessible Page apps with a non-empty NavPlacement: those whose minimum role <paramref name="role"/>
	/// meets and whose permission, if any, <paramref name="holds"/> admits.
	/// </summary>
	private static IEnumerable<PortalApplication> Accessible(IEnumerable<PortalApplication> apps, PortalRole role, Func<string, bool> holds) =>
		apps.Where(a => a.KindEnum == ApplicationKind.Page
			&& !string.IsNullOrWhiteSpace(a.NavPlacement)
			&& a.Admits(role, holds));

	private static bool HoldsNothing(string scope) => false;

	/// <summary>The role-based <c>AppsForSection</c> for a signed-in principal, honouring each app's permission.</summary>
	public static IReadOnlyList<PortalApplication> AppsForSection(
		IEnumerable<PortalApplication> apps, ClaimsPrincipal? user, string section) =>
		AppsForSection(apps, PortalRoleHelper.CurrentRole(user), section, scope => PortalApplication.HoldsPermission(user, scope));

	/// <summary>The role-based <c>NovelSections</c> for a signed-in principal, honouring each app's permission.</summary>
	public static IReadOnlyList<string> NovelSections(IEnumerable<PortalApplication> apps, ClaimsPrincipal? user) =>
		NovelSections(apps, PortalRoleHelper.CurrentRole(user), scope => PortalApplication.HoldsPermission(user, scope));

	/// <summary>
	/// The accessible apps placed in <paramref name="section"/> (case-insensitive match on NavPlacement),
	/// ordered by <c>Order</c> then display name. A role alone holds no permission, so a permission-gated
	/// app is left out.
	/// </summary>
	public static IReadOnlyList<PortalApplication> AppsForSection(
		IEnumerable<PortalApplication> apps, PortalRole role, string section, Func<string, bool>? holds = null) =>
		Accessible(apps, role, holds ?? HoldsNothing)
			.Where(a => string.Equals(a.NavPlacement, section, StringComparison.OrdinalIgnoreCase))
			.OrderBy(a => a.Order)
			.ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>
	/// The novel sections (NavPlacement names that are not one of the built-ins) that have at least one
	/// accessible app, ordered by the minimum <c>Order</c> across their apps (ties broken by section name).
	/// Returned values are the canonical (first-seen) casing of each section name.
	/// </summary>
	public static IReadOnlyList<string> NovelSections(IEnumerable<PortalApplication> apps, PortalRole role, Func<string, bool>? holds = null)
	{
		var builtIn = new HashSet<string>(BuiltInSections, StringComparer.OrdinalIgnoreCase);

		return Accessible(apps, role, holds ?? HoldsNothing)
			.Where(a => !builtIn.Contains(a.NavPlacement!))
			.GroupBy(a => a.NavPlacement!, StringComparer.OrdinalIgnoreCase)
			.Select(g => (Name: g.First().NavPlacement!, MinOrder: g.Min(a => a.Order)))
			.OrderBy(s => s.MinOrder)
			.ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
			.Select(s => s.Name)
			.ToList();
	}
}
