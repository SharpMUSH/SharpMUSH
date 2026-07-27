using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class AccountAdminDbTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task Status_RoundTrip()
	{
		var account = await Db.CreateAccountAsync("disable-test-user", null, "hash-abc");
		await Assert.That(account.Status).IsEqualTo(AccountStatus.Active);

		foreach (var status in (AccountStatus[])[AccountStatus.Disabled, AccountStatus.Closed, AccountStatus.Deleted, AccountStatus.Active])
		{
			await Db.UpdateAccountStatusAsync(account.Id!, status);
			var reloaded = await Db.GetAccountByIdAsync(account.Id!);
			await Assert.That(reloaded!.Status).IsEqualTo(status);
		}
	}

	/// <summary>
	/// The invariant the wiki-attribution design rests on: leaving Active never removes the
	/// document, and never discards the credentials needed to restore it.
	/// </summary>
	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task MarkingDeleted_RetainsTheDocumentAndItsCredentials()
	{
		var account = await Db.CreateAccountAsync("retention-test-user", "retain@example.com", "hash-retain");

		await Db.UpdateAccountStatusAsync(account.Id!, AccountStatus.Deleted);

		var reloaded = await Db.GetAccountByIdAsync(account.Id!);
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Deleted);
		await Assert.That(reloaded.Username).IsEqualTo("retention-test-user");
		await Assert.That(reloaded.Email).IsEqualTo("retain@example.com");
		await Assert.That(reloaded.PasswordHash).IsEqualTo("hash-retain");
	}

	[Test, NotInParallel(nameof(AccountAdminDbTests))]
	public async Task GetAllAccounts_IncludesCreated()
	{
		var account = await Db.CreateAccountAsync("list-test-user", null, "hash-def");
		var all = await Db.GetAllAccountsAsync();
		await Assert.That(all.Any(a => a.Id == account.Id)).IsTrue();
	}
}
