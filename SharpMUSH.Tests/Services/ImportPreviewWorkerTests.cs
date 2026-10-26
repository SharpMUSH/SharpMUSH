using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
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
		var configuration = await staged.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(configuration?.Database.PackageManager).IsNotNull();
		var manager = await staged.GetObjectNodeAsync(new DBRef((int)configuration!.Database.PackageManager!.Value));
		await Assert.That(manager is AnySharpObject and SharpPlayer).IsTrue();
		await Assert.That(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, "live", "data.mdb"))))).IsEqualTo(hash);
		await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(source))).IsEquivalentTo(sourceHash);
	}

	/// <summary>
	/// The shipped mushcnf.dst names the seeded #7 as package manager; in the sample world #7 is an exit. The preview
	/// says so and makes a manager of its own instead of failing.
	/// </summary>
	[Test]
	public async Task PackageManagerNamingANonPlayerWarnsAndCreatesOne()
	{
		var (journal, source, _) = await SetupAsync();
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH),
			"package_manager 7");
		await Assert.That(preview.Failure).IsNull();
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await Assert.That(preview.Result!.Notes).Contains(note => note.StartsWith("The configuration's package_manager is #7, which is not a player in the imported database"));
		await using var staged = Open(preview.WorldPath);
		var configuration = await staged.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(configuration?.Database.PackageManager).IsNotEqualTo(7u);
		var manager = await staged.GetObjectNodeAsync(new DBRef((int)configuration!.Database.PackageManager!.Value));
		await Assert.That(manager is AnySharpObject and SharpPlayer).IsTrue();
	}

	/// <summary>
	/// A gzipped dump, as PennMUSH writes by default, previews as the plain one does, and the configuration lines the
	/// import could not carry over are in the report: the include it did not follow as a warning, the directive
	/// SharpMUSH has no option for as a note.
	/// </summary>
	[Test]
	public async Task GzippedSourcePreviewsAndTheConfigurationReportsWhatItLeftOut()
	{
		var (journal, source, _) = await SetupAsync();
		var compressed = source + ".gz";
		await using (var input = File.OpenRead(source))
		await using (var output = File.Create(compressed))
		await using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
			await input.CopyToAsync(gzip);
		var compressedHash = SHA256.HashData(await File.ReadAllBytesAsync(compressed));
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(compressed, Format: DatabaseImportFormat.RhostMUSH),
			"chunk_swap_file data/chunkswap\ninclude alias.cnf\nmud_name Gzipped");
		await Assert.That(preview.Failure).IsNull();
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await Assert.That(preview.Result!.TotalObjects).IsGreaterThan(0);
		await Assert.That(preview.Result.Warnings.Texts()).Contains(warning => warning.StartsWith("Configuration: include alias.cnf: not followed"));
		await Assert.That(preview.Result.Notes).Contains("Configuration: 1 directive(s) SharpMUSH has no option for, not carried over: chunk_swap_file.");
		await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(compressed))).IsEquivalentTo(compressedHash);
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

	[Test]
	[NotInParallel]
	public async Task WorkerCompletionDoesNotExposeReadyBeforeParentSourceValidation()
	{
		var (journal, source, _) = await SetupAsync();
		var session = journal.Create();
		var privateSource = Path.Combine(session.Directory, "world.source");
		File.Copy(source, privateSource);
		var request = new ImportWorkerRequest(journal.LiveWorldPath, journal.LiveKeyRingPath,
			new DatabaseImportFiles(privateSource, Format: DatabaseImportFormat.RhostMUSH), null);
		await File.WriteAllTextAsync(Path.Combine(session.Directory, "request.json"), System.Text.Json.JsonSerializer.Serialize(request));
		var providerProperty = typeof(Quartz.Logging.LogProvider).GetProperty("CurrentLogProvider",
			System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
			?? throw new InvalidOperationException("Quartz logging provider introspection is unavailable.");
		var originalLogProvider = providerProperty.GetValue(null);
		Quartz.Logging.LogContext.SetCurrentLogProvider(NullLoggerFactory.Instance);
		var parentLogProvider = providerProperty.GetValue(null);
		try
		{
			await RunWorkerProcessAsync(session.Directory);
			// Quartz creates another logger for every timer job. A disposed worker-owned factory
			// here would prevent the live host's timers from firing after this test completes.
			var currentLogProvider = providerProperty.GetValue(null);
			if (currentLogProvider is Quartz.Logging.ILogProvider logger) _ = logger.GetLogger("LiveTimerAfterImportWorker");
			await Assert.That(currentLogProvider).IsSameReferenceAs(parentLogProvider);
		}
		finally { Quartz.Logging.LogProvider.SetCurrentLogProvider((Quartz.Logging.ILogProvider)originalLogProvider!); }
		await Assert.That(journal.Get(session.Id).State).IsEqualTo(ImportSessionState.Validating);
		await Assert.That(() => journal.RequestPromotion(session.Id)).Throws<InvalidOperationException>();
	}

	private static async Task RunWorkerProcessAsync(string directory)
	{
		var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Test entry assembly is unavailable.");
		var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = directory,
			ArgumentList =
			{
				"exec", "--runtimeconfig", Path.ChangeExtension(entry, ".runtimeconfig.json"),
				"--depsfile", Path.ChangeExtension(entry, ".deps.json"),
				typeof(SharpMUSH.Server.Program).Assembly.Location, "--import-worker", directory
			}
		};
		using var process = Process.Start(start) ?? throw new IOException("The import worker did not start.");
		var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
		var error = process.StandardError.ReadToEndAsync();
		using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
		try { await process.WaitForExitAsync(deadline.Token); }
		finally
		{
			if (!process.HasExited) process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync(CancellationToken.None);
			await Task.WhenAll(output, error);
		}
		await Assert.That(process.ExitCode).IsEqualTo(0).Because(await error);
	}

	[Test]
	[NotInParallel]
	public async Task PrivateHostUsesTheSameConfiguredMapCeilingAsThePromotedHost()
	{
		var (journal, _, _) = await SetupAsync();
		var session = journal.Create();
		var previous = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_MAPSIZE");
		try
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_MAPSIZE", (256L << 20).ToString(System.Globalization.CultureInfo.InvariantCulture));
			var host = await new IsolatedImportHostFactory().CreateAsync(session);
			await using var owned = (IAsyncDisposable)host;
			var capacity = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IStorageCapacityService>(host.Services).Measure();
			await Assert.That(capacity.MapSizeBytes).IsEqualTo(256L << 20);
			await Assert.That(capacity.WorldPath).IsEqualTo(session.WorldPath);
		}
		finally { Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_MAPSIZE", previous); }
	}

	[Test]
	public async Task RealWorkerPromotionPreservesClaimedAdministratorAndLatestCredential()
	{
		var (journal, source, _) = await SetupAsync();
		string accountId;
		await using (var live = Open(journal.LiveWorldPath))
		{
			var account = await live.CreateAccountAsync("claimed", "claimed@example.test", "before-preview");
			accountId = account.Id ?? throw new InvalidOperationException("Account id missing.");
			await live.LinkCharacterToAccountAsync(accountId, new DBRef(1));
			await live.SetServerSetupCompletedAsync(true);
			await live.SetExpandedServerData("SetupWizardState", new { Pending = true });
		}
		var coordinator = new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance);
		var preview = await coordinator.PreviewAsync(new DatabaseImportFiles(source, Format: DatabaseImportFormat.RhostMUSH),
			"mud_name Imported Identity Name");
		await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
		await using (var live = Open(journal.LiveWorldPath))
		{
			await live.UpdateAccountPasswordAsync(accountId, "after-preview");
		}
		string? importedPassword;
		await using (var staged = Open(preview.WorldPath))
		{
			importedPassword = (await staged.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>().PasswordHash;
		}
		journal.RequestPromotion(preview.Id);
		journal.ApplyPendingBeforeHostConstruction();
		await using var promoted = Open(journal.LiveWorldPath);
		await Assert.That((await promoted.GetAccountByUsernameAsync("claimed"))?.PasswordHash).IsEqualTo("after-preview");
		await Assert.That((await promoted.GetServerStateAsync()).SetupCompleted).IsTrue();
		await Assert.That((await promoted.GetExpandedServerData<SharpMUSH.Server.Services.SetupWizardState>("SetupWizardState"))?.Pending ?? false).IsTrue();
		await Assert.That((await promoted.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))?.Net.MudName).IsEqualTo("Imported Identity Name");
		await Assert.That((await promoted.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>().PasswordHash).IsEqualTo(importedPassword);
	}

}
