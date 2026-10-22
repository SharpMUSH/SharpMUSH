using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The portal administrator who claimed the game keeps their sign-in through an import's promotion (#2078): the
/// promotion carries the live world's accounts into the imported one, linked to the imported #1, and the promoted
/// world neither offers the claim again nor needs a new unclaimed administrator.
/// </summary>
public class ImportAdministratorTransferTests
{
	private readonly string _root = Path.Join(Path.GetTempPath(), "import-admin-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private async Task<(ImportPromotionJournal Journal, string Source)> SetupAsync()
	{
		Directory.CreateDirectory(_root);
		var source = Path.Join(_root, "source.flat");
		File.Copy(Path.Join(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"), source);
		return (new ImportPromotionJournal(Path.Join(_root, "live")), source);
	}

	[Test]
	public async Task ClaimedAdministratorSignsInToThePromotedWorld()
	{
		var (journal, source) = await SetupAsync();
		var credential = RandomNumberGenerator.GetBytes(32);
		var identity = new ExternalIdentityKey("github", "https://github.com", "4242");
		await using (var live = Open(journal.LiveWorldPath))
		{
			await live.Migrate();
			var account = await live.CreateAccountAsync("Claimer", "claimer@example.com", "claimed-password-hash");
			await live.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));
			await live.UpdateAccountCharacterPasswordLoginAsync(account.Id!, false);
			await live.AddAccountPasskeyAsync(new AccountPasskey(account.Id!, credential, [1, 2, 3], 7, "Laptop", ["internal"], true,
				DateTimeOffset.UnixEpoch, null), 10);
			await Assert.That(await live.AttachExternalIdentityAsync(account.Id!, identity, "claimer") is ExternalIdentity).IsTrue();
			await live.UpsertRoleAsync(new SharpRole { Slug = "live-only", Name = "Live Only" });
			await live.AssignRoleToAccountAsync(account.Id!, BuiltInRoles.WizardSlug);
			await live.AssignRoleToAccountAsync(account.Id!, "live-only");
			await live.SetAccountOverrideAsync(account.Id!, "wiki.admin", PermissionState.Allow);
			await live.SetServerSetupCompletedAsync(true);
			await live.SetExpandedServerData(nameof(SetupWizardState), new SetupWizardState { Pending = true });
		}

		var preview = await new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance)
			.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH));
		await Assert.That(preview.Failure).IsNull();
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await Assert.That(File.Exists(Path.Join(preview.Directory, "request.json"))).IsFalse();

		journal.RequestPromotion(preview.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var promoted = Open(journal.LiveWorldPath);
		await Assert.That((await promoted.GetServerStateAsync()).SetupCompleted).IsTrue();
		if (await promoted.GetAccountForCharacterAsync(new DBRef(1)) is not { } carried)
			throw new InvalidOperationException("#1 has no account in the promoted world.");
		await Assert.That(carried.Username).IsEqualTo("Claimer");
		await Assert.That(carried.Email).IsEqualTo("claimer@example.com");
		await Assert.That(carried.PasswordHash).IsEqualTo("claimed-password-hash");
		await Assert.That(carried.AllowCharacterPasswordAccountLogin).IsFalse();
		var passkey = await promoted.GetAccountPasskeyAsync(credential);
		await Assert.That(passkey?.AccountId).IsEqualTo(carried.Id);
		await Assert.That(passkey!.SignCount).IsEqualTo(7u);
		await Assert.That((await promoted.GetExternalIdentityAsync(identity)).Expect<ExternalIdentity>().AccountId).IsEqualTo(carried.Id);
		await Assert.That((await promoted.GetRolesForAccountAsync(carried.Id!)).Select(role => role.Slug).ToArray())
			.IsEquivalentTo([BuiltInRoles.WizardSlug, "live-only"]);
		await Assert.That((await promoted.GetAccountOverridesAsync(carried.Id!))["wiki.admin"]).IsEqualTo(PermissionState.Allow);
		await Assert.That((await promoted.GetExpandedServerData<SetupWizardState>(nameof(SetupWizardState)))?.Pending).IsTrue();
		// BootstrapService makes an unclaimed administrator only when no one has an account.
		await Assert.That((await promoted.GetAllAccountsAsync()).Count(a => !SystemAccount.IsReserved(a.Username))).IsEqualTo(1);
	}
}
