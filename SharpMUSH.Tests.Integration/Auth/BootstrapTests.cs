using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Integration tests for <c>BootstrapService</c>: on a fresh DB it must pre-generate an
/// unclaimed admin account (empty password hash) linked to player #1 (God) — no env vars,
/// no generated password, no log banner.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BootstrapTests(ServerWebAppFactory factory)
{
	[Test]
	public async Task Bootstrap_PreGeneratesUnclaimedAdminLinkedToGod()
	{
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var admin = await accountService.GetAccountForCharacterAsync(new DBRef(1));

		await Assert.That(admin).IsNotNull();
		await Assert.That(admin!.PasswordHash).IsEqualTo(string.Empty);
	}
}
