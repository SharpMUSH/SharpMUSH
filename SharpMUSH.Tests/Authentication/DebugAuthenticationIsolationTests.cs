using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Authentication;

public class DebugAuthenticationIsolationTests
{
	[Test]
	public async Task SeparateHosts_ResolveTheirOwnBootstrapIdentity_AndCacheWithinEachHost()
	{
		var firstAccounts = Accounts("first-world");
		var secondAccounts = Accounts("second-world");
		using var first = Host(firstAccounts);
		using var second = Host(secondAccounts);

		await Assert.That(await AccountIdAsync(first)).IsEqualTo("first-world");
		await Assert.That(await AccountIdAsync(second)).IsEqualTo("second-world");
		await Assert.That(await AccountIdAsync(first)).IsEqualTo("first-world");
		await Assert.That(await AccountIdAsync(second)).IsEqualTo("second-world");
		await firstAccounts.Received(1).GetAccountForCharacterAsync(new DBRef(1));
		await secondAccounts.Received(1).GetAccountForCharacterAsync(new DBRef(1));
	}

	private static IAccountService Accounts(string id)
	{
		var accounts = Substitute.For<IAccountService>();
		accounts.GetAccountForCharacterAsync(new DBRef(1)).Returns(new SharpAccount { Id = id, Username = id });
		accounts.GetCharactersAsync(id).Returns((IReadOnlyList<SharpPlayer>)[AdministrativeCapabilityTests.Player(1)]);
		return accounts;
	}

	private static ServiceProvider Host(IAccountService accounts)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMemoryCache();
		services.AddSingleton(accounts);
		services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
			.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(DebugAuthenticationHandler.SchemeName, _ => { });
		return services.BuildServiceProvider();
	}

	private static async Task<string?> AccountIdAsync(ServiceProvider host)
	{
		using var scope = host.CreateScope();
		var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
		var result = await context.AuthenticateAsync();
		return result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
	}
}
