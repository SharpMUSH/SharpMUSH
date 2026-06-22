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

	/// <summary>The accessible Page apps with a non-empty NavPlacement, role-filtered for <paramref name="role"/>.</summary>
	private static IEnumerable<PortalApplication> Accessible(IEnumerable<PortalApplication> apps, PortalRole role) =>
		apps.Where(a => a.KindEnum == ApplicationKind.Page
			&& !string.IsNullOrWhiteSpace(a.NavPlacement)
			&& role >= a.MinimumRoleEnum);

	/// <summary>
	/// The accessible apps placed in <paramref name="section"/> (case-insensitive match on NavPlacement),
	/// ordered by <c>Order</c> then display name.
	/// </summary>
	public static IReadOnlyList<PortalApplication> AppsForSection(
		IEnumerable<PortalApplication> apps, PortalRole role, string section) =>
		Accessible(apps, role)
			.Where(a => string.Equals(a.NavPlacement, section, StringComparison.OrdinalIgnoreCase))
			.OrderBy(a => a.Order)
			.ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>
	/// The novel sections (NavPlacement names that are not one of the built-ins) that have at least one
	/// accessible app, ordered by the minimum <c>Order</c> across their apps (ties broken by section name).
	/// Returned values are the canonical (first-seen) casing of each section name.
	/// </summary>
	public static IReadOnlyList<string> NovelSections(IEnumerable<PortalApplication> apps, PortalRole role)
	{
		var builtIn = new HashSet<string>(BuiltInSections, StringComparer.OrdinalIgnoreCase);

		return Accessible(apps, role)
			.Where(a => !builtIn.Contains(a.NavPlacement!))
			.GroupBy(a => a.NavPlacement!, StringComparer.OrdinalIgnoreCase)
			.Select(g => (Name: g.First().NavPlacement!, MinOrder: g.Min(a => a.Order)))
			.OrderBy(s => s.MinOrder)
			.ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
			.Select(s => s.Name)
			.ToList();
	}
}
