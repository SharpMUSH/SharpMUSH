using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BootstrapService"/>'s first-run guard. The guard has to ignore
/// reserved accounts: once a reserved account exists unconditionally, "any account exists" is
/// permanently true and the admin account would never be pre-generated.
/// </summary>
public class BootstrapServiceTests
{
	private static SharpAccount Account(string username) => new()
	{
		Id = $"node_accounts/{username}",
		Username = username,
		PasswordHash = string.Empty
	};

	private static (BootstrapService Service, IAccountService Accounts) Build(params SharpAccount[] existing)
	{
		var accounts = Substitute.For<IAccountService>();
		accounts.GetAllAccountsAsync(Arg.Any<CancellationToken>()).Returns(existing.ToList());
		accounts.CreateUnclaimedAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => Account(callInfo.Arg<string>()));
		accounts.GetOrCreateSystemAccountAsync(Arg.Any<CancellationToken>())
			.Returns(Account(SystemAccount.Username));
		return (new BootstrapService(accounts, NullLogger<BootstrapService>.Instance), accounts);
	}

	[Test]
	public async ValueTask FreshDatabase_PreGeneratesAdminLinkedToGod()
	{
		var (service, accounts) = Build();

		await service.StartAsync(CancellationToken.None);

		await accounts.Received(1).CreateUnclaimedAccountAsync("admin", Arg.Any<CancellationToken>());
		await accounts.Received(1).LinkCharacterAsync(
			"node_accounts/admin", new DBRef(1), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask ExistingPlayerAccount_SkipsPreGeneration()
	{
		var (service, accounts) = Build(Account("someplayer"));

		await service.StartAsync(CancellationToken.None);

		await accounts.DidNotReceive().CreateUnclaimedAccountAsync(
			Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask Always_EnsuresTheSystemAccountExists()
	{
		var (service, accounts) = Build(Account("someplayer"));

		await service.StartAsync(CancellationToken.None);

		// Runs even when pre-generation is skipped: the system account is not first-run state, it
		// is a permanent fixture that phase-2 attribution hangs off.
		await accounts.Received(1).GetOrCreateSystemAccountAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask OnlyReservedAccountsExist_StillPreGeneratesAdmin()
	{
		var (service, accounts) = Build(Account(SystemAccount.Username));

		await service.StartAsync(CancellationToken.None);

		await accounts.Received(1).CreateUnclaimedAccountAsync("admin", Arg.Any<CancellationToken>());
	}
}
