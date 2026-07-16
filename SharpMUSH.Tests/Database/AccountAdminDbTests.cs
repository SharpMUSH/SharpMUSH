using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;

namespace SharpMUSH.Tests.Database;

public class AccountAdminDbTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task DisableFlag_RoundTrip()
	{
		var account = await Db.CreateAccountAsync("disable-test-user", null, "hash-abc");
		await Db.UpdateAccountDisabledAsync(account.Id!, true);
		var reloaded = await Db.GetAccountByIdAsync(account.Id!);
		await Assert.That(reloaded!.IsDisabled).IsTrue();

		await Db.UpdateAccountDisabledAsync(account.Id!, false);
		reloaded = await Db.GetAccountByIdAsync(account.Id!);
		await Assert.That(reloaded!.IsDisabled).IsFalse();
	}

	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task GetAllAccounts_IncludesCreated()
	{
		var account = await Db.CreateAccountAsync("list-test-user", null, "hash-def");
		var all = await Db.GetAllAccountsAsync();
		await Assert.That(all.Any(a => a.Id == account.Id)).IsTrue();
	}
}
