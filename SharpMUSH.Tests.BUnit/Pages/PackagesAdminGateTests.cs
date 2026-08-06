using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The package admin section must gate on the <c>packages.admin</c> permission, never on a role name.
/// <para>
/// AdminPackageReview gated on <c>[Authorize(Roles = "Wizard")]</c> while its four siblings gated on
/// the policy. Role claims carry no hierarchy — AccountAuthStateProvider emits exactly one
/// <see cref="System.Security.Claims.ClaimTypes.Role"/> claim from the account's role — so a God
/// account holding every permission scope, <c>packages.admin</c> included, failed an exact-match
/// check for "Wizard" and got the not-authorized card on one page out of five.
/// </para>
/// <para>
/// Discovered from the routes rather than a hand-written page list: a sixth package page added with
/// a role gate has to fail here, which a list of five type names would not do.
/// </para>
/// </summary>
public class PackagesAdminGateTests
{
	public static IEnumerable<Type> PackageAdminPages() =>
		typeof(SharpMUSH.Client.Pages.Admin.Packages.AdminPackages).Assembly
			.GetTypes()
			.Where(t => t.Namespace == typeof(SharpMUSH.Client.Pages.Admin.Packages.AdminPackages).Namespace)
			.Where(t => t.GetCustomAttributes<RouteAttribute>().Any())
			.OrderBy(t => t.FullName, StringComparer.Ordinal);

	[TUnit.Core.Test]
	public async Task Every_package_admin_page_is_discovered()
	{
		// Guards the discovery above: a namespace rename would leave every case below unrun and
		// nothing else would notice.
		await Assert.That(PackageAdminPages().Count()).IsGreaterThanOrEqualTo(5);
	}

	[TUnit.Core.Test]
	[MethodDataSource(nameof(PackageAdminPages))]
	public async Task Package_admin_page_gates_on_the_packages_admin_policy(Type page)
	{
		var authorize = page.GetCustomAttributes<AuthorizeAttribute>(inherit: true).SingleOrDefault();

		await Assert.That(authorize).IsNotNull()
			.Because($"{page.Name} administers installable softcode packages");
		await Assert.That(authorize!.Policy).IsEqualTo(PortalPermission.PackagesAdmin);
		await Assert.That(authorize.Roles).IsNull()
			.Because("role claims have no hierarchy here, so a role gate silently excludes higher roles");
	}
}
