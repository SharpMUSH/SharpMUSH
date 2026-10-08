using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Pins that the two admin controllers which were shipped with NO authorization attribute — so
/// <c>POST api/databaseconversion/upload</c> (overwrites the whole game DB) and all of
/// <c>api/suggestion</c> were anonymously reachable — are gated at the class level. Reflection over
/// the attribute is enough to catch a regression that drops the gate; the policy machinery itself is
/// covered elsewhere.
/// </summary>
public class ControllerAuthorizationTests
{
	private static AuthorizeAttribute? ClassAuthorize<T>() =>
		(AuthorizeAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(AuthorizeAttribute));

	[Test]
	public async Task DatabaseConversionController_IsGatedOnServerAdmin()
	{
		var attr = ClassAuthorize<DatabaseConversionController>();

		await Assert.That(attr).IsNotNull();
		await Assert.That(attr!.Policy).IsEqualTo(PortalPermission.ServerAdmin);
	}

	[Test]
	public async Task SuggestionController_IsGatedOnConfigAdmin()
	{
		var attr = ClassAuthorize<SuggestionController>();

		await Assert.That(attr).IsNotNull();
		await Assert.That(attr!.Policy).IsEqualTo(PortalPermission.ConfigAdmin);
	}

	/// <summary>The character list reads with players.view; booting from it and the audit log take players.moderate.</summary>
	[Test]
	public async Task StaffControllers_AreGatedOnTheirScopes()
	{
		await Assert.That(ClassAuthorize<AdminCharactersController>()!.Policy).IsEqualTo(PortalPermission.PlayersView);
		await Assert.That(ClassAuthorize<AdminAuditController>()!.Policy).IsEqualTo(PortalPermission.PlayersModerate);

		var boot = typeof(AdminCharactersController).GetMethod(nameof(AdminCharactersController.Boot))!;
		var attr = (AuthorizeAttribute?)Attribute.GetCustomAttribute(boot, typeof(AuthorizeAttribute));
		await Assert.That(attr?.Policy).IsEqualTo(PortalPermission.PlayersModerate);
	}

	/// <summary>
	/// Every package operation takes packages.admin. The actions carried only a bare [Authorize], so any signed-in
	/// account could add a remote and apply from it, and a softcode package can create WIZARD objects.
	/// </summary>
	[Test]
	public async Task PackagesController_IsGatedOnPackagesAdmin()
	{
		await Assert.That(ClassAuthorize<PackagesController>()?.Policy).IsEqualTo(PortalPermission.PackagesAdmin);

		var anonymous = typeof(PackagesController).GetMethods()
			.Where(method => Attribute.IsDefined(method, typeof(AllowAnonymousAttribute)))
			.Select(method => method.Name)
			.ToList();
		await Assert.That(anonymous).IsEmpty();
	}
}
