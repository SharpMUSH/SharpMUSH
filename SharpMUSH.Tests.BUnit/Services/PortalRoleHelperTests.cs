using System.Security.Claims;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A principal can carry more than one role claim: the development principal leads with <c>Admin</c>, which
/// is no <see cref="PortalRole"/>, and then claims every role. Reading only the first claim made that
/// principal a Guest everywhere a role is compared.
/// </summary>
public class PortalRoleHelperTests
{
	private static ClaimsPrincipal With(params string[] roles) =>
		new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

	[Test]
	public async Task TheHighestRecognizedRole_Wins_WhateverClaimComesFirst()
	{
		var debug = With(["Admin", .. Enum.GetNames<PortalRole>()]);

		await Assert.That(PortalRoleHelper.CurrentRole(debug)).IsEqualTo(PortalRole.God);
	}

	[Test]
	public async Task OneRecognizedRole_IsThatRole()
	{
		await Assert.That(PortalRoleHelper.CurrentRole(With("Builder"))).IsEqualTo(PortalRole.Builder);
	}

	[Test]
	public async Task NoRecognizedRole_IsGuest()
	{
		await Assert.That(PortalRoleHelper.CurrentRole(With("Admin"))).IsEqualTo(PortalRole.Guest);
		await Assert.That(PortalRoleHelper.CurrentRole(new ClaimsPrincipal(new ClaimsIdentity()))).IsEqualTo(PortalRole.Guest);
		await Assert.That(PortalRoleHelper.CurrentRole(null)).IsEqualTo(PortalRole.Guest);
	}
}
