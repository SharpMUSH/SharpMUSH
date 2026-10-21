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
/// The portal administrator who claimed the game keeps their sign-in through an import's promotion (#2078): the real
/// worker writes the account into the world it builds, linked to the imported #1, and the promoted world neither
/// offers the claim again nor needs a new unclaimed administrator.
/// </summary>
public class ImportAdministratorTransferTests
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "import-admin-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static IExpandedObjectDataService Wizard(bool pending)
	{
		var data = Substitute.For<IExpandedObjectDataService>();
		data.GetExpandedServerDataAsync<SetupWizardState>().Returns(new SetupWizardState { Pending = pending });
		return data;
	}

	private static PortalAdministratorTransfer Transfer(LightningDatabase world, bool pending = false)
		=> new(world, world, world, world, Wizard(pending));

	private async Task<(ImportPromotionJournal Journal, string Source)> SetupAsync()
	{
		Directory.CreateDirectory(_root);
		var source = Path.Combine(_root, "source.flat");
		File.Copy(Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"), source);
		return (new ImportPromotionJournal(Path.Combine(_root, "live")), source);
	}

	[Test]
	public async Task UnclaimedGameCarriesNoAdministrator()
	{
		var (journal, _) = await SetupAsync();
		await using var live = Open(journal.LiveWorldPath);
		await live.Migrate();
		var account = await live.CreateAccountAsync("admin", null, "");
		await live.LinkCharacterToAccountAsync(account.Id!, new DBRef(1));

		await Assert.That(await Transfer(live).CaptureAsync()).IsNull();
		await live.SetServerSetupCompletedAsync(true);
		await Assert.That(await Transfer(live).CaptureAsync()).IsNull().Because("an account with no way to sign in has nothing to carry");
	}

	[Test]
	public async Task ClaimedAdministratorSignsInToThePromotedWorld()
	{
		var (journal, source) = await SetupAsync();
		var credential = RandomNumberGenerator.GetBytes(32);
		var identity = new ExternalIdentityKey("github", "https://github.com", "4242");
		PortalAdministrator administrator;
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
			administrator = (await Transfer(live, pending: true).CaptureAsync())!;
		}
		await Assert.That(administrator).IsNotNull();

		var preview = await new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance)
			.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH), administrator: administrator);
		await Assert.That(preview.Failure).IsNull();
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await Assert.That(preview.Result!.Warnings).Contains("The imported world has no role 'live-only', so Claimer does not hold it.");
		await Assert.That(File.Exists(Path.Combine(preview.Directory, "request.json"))).IsFalse();

		journal.RequestPromotion(preview.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var promoted = Open(journal.LiveWorldPath);
		await Assert.That((await promoted.GetServerStateAsync()).SetupCompleted).IsTrue();
		var carried = await promoted.GetAccountForCharacterAsync(new DBRef(1));
		await Assert.That(carried?.Username).IsEqualTo("Claimer");
		await Assert.That(carried!.Email).IsEqualTo("claimer@example.com");
		await Assert.That(carried.PasswordHash).IsEqualTo("claimed-password-hash");
		await Assert.That(carried.AllowCharacterPasswordAccountLogin).IsFalse();
		var passkey = await promoted.GetAccountPasskeyAsync(credential);
		await Assert.That(passkey?.AccountId).IsEqualTo(carried.Id);
		await Assert.That(passkey!.SignCount).IsEqualTo(7u);
		await Assert.That((await promoted.GetExternalIdentityAsync(identity)).Expect<ExternalIdentity>().AccountId).IsEqualTo(carried.Id);
		await Assert.That((await promoted.GetRolesForAccountAsync(carried.Id!)).Select(role => role.Slug).ToArray())
			.IsEquivalentTo([BuiltInRoles.WizardSlug]);
		await Assert.That((await promoted.GetAccountOverridesAsync(carried.Id!))["wiki.admin"]).IsEqualTo(PermissionState.Allow);
		await Assert.That((await promoted.GetExpandedServerData<SetupWizardState>(nameof(SetupWizardState)))?.Pending).IsTrue();
		// BootstrapService makes an unclaimed administrator only when no one has an account.
		await Assert.That((await promoted.GetAllAccountsAsync()).Count(a => !SystemAccount.IsReserved(a.Username))).IsEqualTo(1);
	}
}
