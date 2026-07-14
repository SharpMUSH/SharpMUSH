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
	// Order = 0 is the earliest slot in the "SetupFlow" constraint group and must stay unique:
	// this test asserts the bootstrap admin's password hash is still empty, so it has to observe
	// state before SetupFlowTests (Order = 1..5) or AdminAccountsApiTests (Order = 6) claim it.
	// TUnit's ConstraintKeyScheduler orders same-key tests by Order, with ties broken by
	// discovery order (unspecified across classes) — see AdminAccountsApiTests' class doc for the
	// prior Order = 0 collision this caused.
	[Test, NotInParallel("SetupFlow", Order = 0)]
	public async Task Bootstrap_PreGeneratesUnclaimedAdminLinkedToGod()
	{
		var accountService = factory.Services.GetRequiredService<IAccountService>();
		var admin = await accountService.GetAccountForCharacterAsync(new DBRef(1));

		await Assert.That(admin).IsNotNull();
		await Assert.That(admin!.PasswordHash).IsEqualTo(string.Empty);
	}
}
