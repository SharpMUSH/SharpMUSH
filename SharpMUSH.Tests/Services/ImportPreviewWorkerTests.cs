using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Services;

public class ImportPreviewWorkerTests
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "import-worker-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	private static LightningDatabase Open(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private async Task<(ImportPromotionJournal Journal, string Source, string Hash)> SetupAsync()
	{
		Directory.CreateDirectory(_root);
		var live = Path.Combine(_root, "live");
		await using (var world = Open(live))
		{
			await world.Migrate();
			await world.CreateRoomAsync("Outgoing world sentinel", (await world.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>());
		}
		var source = Path.Combine(_root, "source.flat");
		File.Copy(Path.Combine(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat"), source);
		return (new ImportPromotionJournal(live), source, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(live, "data.mdb")))));
	}

	[Test]
	public async Task RealWorkerPreviewWritesItsOwnWorldAndNeverChangesLiveOrTheSource()
	{
		var (journal, source, hash) = await SetupAsync();
		var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH));
		await Assert.That(preview.Failure).IsNull().Because(File.Exists(Path.Combine(preview.Directory, "worker-error.log")) ? await File.ReadAllTextAsync(Path.Combine(preview.Directory, "worker-error.log")) : "the actual worker should complete");
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await Assert.That(preview.Result!.TotalObjects).IsGreaterThan(0);
		await using var staged = Open(preview.WorldPath);
		await Assert.That(await staged.GetObjectCountAsync()).IsGreaterThan(0);
		await Assert.That(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, "live", "data.mdb"))))).IsEqualTo(hash);
		await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(source))).IsEquivalentTo(sourceHash);
	}

	[Test]
	public async Task MalformedDeclaredMailAbortsBeforeWorldWritesAndPreservesBothSources()
	{
		var (journal, source, hash) = await SetupAsync();
		var mail = Path.Combine(_root, "broken.mail");
		await File.WriteAllTextAsync(mail, "truncated declared mail");
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(source, Mail: mail, Format: DatabaseImportFormat.RhostMUSH));
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Aborted);
		await Assert.That(preview.Result!.Errors).IsNotEmpty();
		await Assert.That(Directory.Exists(preview.WorldPath)).IsFalse();
		await Assert.That(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, "live", "data.mdb"))))).IsEqualTo(hash);
		await Assert.That(await File.ReadAllTextAsync(mail)).IsEqualTo("truncated declared mail");
	}
	[Test]
	public async Task PreviewConfigurationAndCredentialsStayInThePrivateWorldAndSurvivePromotionTogether()
	{
		var (journal, source, hash) = await SetupAsync();
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		const string secret = "synthetic-import-only-password";
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH),
			"mud_name Private Imported Name\nsql_password " + secret);
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await using (var staged = Open(preview.WorldPath))
		{
			var options = await staged.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
			await Assert.That(options?.Net.MudName).IsEqualTo("Private Imported Name");
			var encrypted = await staged.GetExpandedServerData<SecretsData>(nameof(SecretsData));
			await Assert.That(encrypted?.Secrets.ContainsKey("sql.password")).IsTrue();
		}
		await Assert.That(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, "live", "data.mdb"))))).IsEqualTo(hash);
		await Assert.That(File.Exists(preview.ConfigurationPath)).IsFalse();
		await Assert.That(File.Exists(Path.Combine(preview.Directory, "request.json"))).IsFalse();
		journal.RequestPromotion(preview.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var promoted = Open(journal.LiveWorldPath);
		var secrets = await promoted.GetExpandedServerData<SecretsData>(nameof(SecretsData));
		var cipher = secrets?.Secrets["sql.password"].Ciphertext ?? throw new InvalidOperationException("Imported secret missing.");
		var protection = DataProtectionProvider.Create(new DirectoryInfo(journal.LiveKeyRingPath), options => options.SetApplicationName("SharpMUSH"));
		await Assert.That(protection.CreateProtector("SharpMUSH.Secrets", "sql.password").Unprotect(cipher)).IsEqualTo(secret);
	}

	[Test]
	public async Task CancelledPreviewCleansOnlyItsPrivateWorldAndLeavesLiveConfigurationUntouched()
	{
		var (journal, source, hash) = await SetupAsync();
		var preview = await new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance)
			.PreviewAsync(new DatabaseImportFiles(source), "mud_name Cancelled", cancellationToken: new CancellationToken(true));
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Aborted);
		await Assert.That(Directory.Exists(preview.WorldPath)).IsFalse();
		await Assert.That(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, "live", "data.mdb"))))).IsEqualTo(hash);
	}

}
