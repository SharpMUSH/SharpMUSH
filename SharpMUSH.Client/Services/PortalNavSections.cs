using System.Security.Claims;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The page applications one sidebar lists: a registered Page application's
/// <see cref="PortalApplication.NavPlacement"/> names the <b>section</b> its link belongs to. Build and Manage
/// apps are listed in Build &amp; manage, Play apps at the foot of the Play sidebar, and a section of its own
/// is that section's sidebar. Where the main navigation puts each section is <c>NavResolver</c>'s business.
///
/// <para>UI-free so it can be unit-tested directly: it filters the accessible Page apps for a caller's
/// <see cref="PortalRole"/> and returns the apps for a given section in <c>Order</c>-then-name order.</para>
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
}
