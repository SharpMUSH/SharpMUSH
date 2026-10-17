using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;
using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Tools.RhostExport;

namespace SharpMUSH.Tests.Services;

/// <summary>Fixtures written and exported by the pinned upstream QDBM and MDBX converters.</summary>
public class RhostNativeExportImportTests
{
	private static string FixtureRoot => Environment.GetEnvironmentVariable("RHOST_FIXTURE_ROOT") ?? Path.Join(AppContext.BaseDirectory, "Services", "TestData", "RhostNative");

	[Test]
	public async Task CanonicalComparisonDetectsChangedUserAttributeKeys()
	{
		var definition = new RhostAttributeDefinition(256, "TEST", 0);
		var original = new RhostFlatfile(0, 0, 257, new Dictionary<int, RhostAttributeDefinition> { [256] = definition }, []);
		var changed = original with { UserAttributes = new Dictionary<int, RhostAttributeDefinition> { [257] = definition } };
		await Assert.That(CanonicalModel(changed)).IsNotEqualTo(CanonicalModel(original));
	}

	[Test]
	[Arguments("legacy")]
	[Arguments("mdbx")]
	public async Task GenuineExportsPreserveEveryParsedSeedField(string backend)
	{
		var seed = await RhostFlatfileReader.ReadFileStrictAsync(Path.Join(FixtureRoot, "seed.flat"));
		var exported = await RhostFlatfileReader.ReadFileStrictAsync(Path.Join(FixtureRoot, backend, "export.flat"));
		await Assert.That(CanonicalModel(exported)).IsEqualTo(CanonicalModel(seed));
	}

	[Test]
	[Arguments("legacy")]
	[Arguments("mdbx")]
	public async Task CompressedNativeFixturesMatchRecordedUpstreamInventory(string backend)
	{
		using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(FixtureRoot, "provenance.json")));
		var entry = provenance.RootElement.GetProperty("backends").GetProperty(backend);
		await AssertHashAsync(Path.Join(FixtureRoot, backend, "export.flat"), entry.GetProperty("export_sha256").GetString()!);
		await AssertHashAsync(Path.Join(FixtureRoot, "seed.flat"), provenance.RootElement.GetProperty("seed_sha256").GetString()!);
		foreach (var file in entry.GetProperty("native").EnumerateObject())
		{
			await using var compressed = File.OpenRead(Path.Join(FixtureRoot, backend, file.Name + ".gz"));
			await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
			using var contents = new MemoryStream();
			await gzip.CopyToAsync(contents);
			await Assert.That(contents.Length).IsEqualTo(file.Value.GetProperty("bytes").GetInt64());
			await Assert.That(Convert.ToHexStringLower(SHA256.HashData(contents.ToArray())))
				.IsEqualTo(file.Value.GetProperty("sha256").GetString());
		}
	}

	[Test]
	[Arguments("legacy")]
	[Arguments("mdbx")]
	public async Task GenuineExportsImportWithPortableMailIntoAnIsolatedWorld(string backend)
		=> await AssertImportedWorldAsync(Path.Join(FixtureRoot, backend, "export.flat"));

	[Test]
	[Arguments("legacy", "LEGACY_QDBM", RhostBackend.LegacyQdbm)]
	[Arguments("mdbx", "MDBX", RhostBackend.Mdbx)]
	public async Task RealUpstreamExporterRoundTripsNativeBackup(string backend, string environmentSuffix, RhostBackend kind)
	{
		var exporter = Environment.GetEnvironmentVariable("RHOST_EXPORTER_" + environmentSuffix);
		var profile = Environment.GetEnvironmentVariable("RHOST_PROFILE_" + environmentSuffix);
		if (string.IsNullOrWhiteSpace(exporter) || string.IsNullOrWhiteSpace(profile))
		{
			Skip.Test($"Set RHOST_EXPORTER_{environmentSuffix} and RHOST_PROFILE_{environmentSuffix} to run the real pinned upstream converter.");
			return;
		}

		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Join(AppContext.BaseDirectory, "work");
		var root = Path.Join(scratch, "rhost-native-" + Guid.NewGuid().ToString("N"));
		var source = Path.Join(root, "source");
		Directory.CreateDirectory(source);
		try
		{
			foreach (var archive in Directory.EnumerateFiles(Path.Join(FixtureRoot, backend), "*.gz"))
			{
				await using var compressed = File.OpenRead(archive);
				await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
				await using var destination = File.Create(Path.Join(source, Path.GetFileNameWithoutExtension(archive)));
				await gzip.CopyToAsync(destination);
			}
			// The historical hand-made fixture understates this body (25 rather than 42 raw UTF-8 bytes).
			// Correct only the portable companion copy that the strict offline exporter reads.
			await File.WriteAllTextAsync(Path.Join(source, "portable.mail"),
				(await File.ReadAllTextAsync(RhostFlatfileReaderTests.MailPath)).Replace("3/1792037260/1/25/", "3/1792037260/1/42/", StringComparison.Ordinal));
			File.Copy(RhostFlatfileReaderTests.FolderPath, Path.Join(source, "portable.folder"));
			var before = await SourceHashesAsync(source);
			var result = await new RhostOfflineExporter().ExportAsync(new RhostExportOptions
			{
				SourceDirectory = source,
				DatabaseBase = "netrhost.gdbm",
				Checkpoint = "netrhost.db",
				ExporterPath = exporter,
				ExporterProfilePath = profile,
				OutputDirectory = Path.Join(root, "export"),
				Backend = kind,
				// These are independent portable mail fixtures, not native mail generated by dbconvert.
				MailFile = "portable.mail",
				FolderFile = "portable.folder"
			});
			await Assert.That(await SourceHashesAsync(source)).IsEqualTo(before);
			var expected = await RhostFlatfileReader.ReadFileStrictAsync(Path.Join(FixtureRoot, backend, "export.flat"));
			var actualPath = Path.Join(result.OutputDirectory, "world.flat");
			var actual = await RhostFlatfileReader.ReadFileStrictAsync(actualPath);
			await Assert.That(CanonicalModel(actual)).IsEqualTo(CanonicalModel(expected));
			await Assert.That(result.ObjectCount).IsEqualTo(expected.Objects.Count);
			await Assert.That(result.AttributeCount).IsEqualTo(expected.Objects.Sum(x => x.Attributes.Count));
			await AssertImportedWorldAsync(actualPath, Path.Join(result.OutputDirectory, "mail.dump"),
				Path.Join(result.OutputDirectory, "folders.dump"));
			var live = Path.Combine(root, "live");
			await using (var outgoing = OpenPromoted(live)) await outgoing.Migrate();
			var liveBefore = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(live, "data.mdb")));
			var journal = new ImportPromotionJournal(live);
			var preview = await new ImportPreviewCoordinator(journal, NullLogger<ImportPreviewCoordinator>.Instance)
				.PreviewAsync(new DatabaseImportFiles(actualPath, Path.Join(result.OutputDirectory, "mail.dump"),
					MailFolders: Path.Join(result.OutputDirectory, "folders.dump"), Format: DatabaseImportFormat.RhostMUSH));
			await Assert.That(preview.State).IsEqualTo(ImportSessionState.Ready);
			await Assert.That(preview.Result!.Warnings.Any(warning => warning.Contains("Destroyed objects (garbage) not imported", StringComparison.Ordinal))).IsTrue();
			await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(live, "data.mdb")))).IsEquivalentTo(liveBefore);
			await Assert.That(await SourceHashesAsync(source)).IsEqualTo(before);
			journal.RequestPromotion(preview.Id);
			journal.ApplyPendingBeforeHostConstruction();
			await using var promoted = OpenPromoted(live);
			await Assert.That(await promoted.GetObjectCountAsync()).IsEqualTo(expected.Objects.Count(obj => obj.Type != RhostObjectType.Garbage));
			await Assert.That(await promoted.GetAllObjectsAsync().AnyAsync(obj => obj.Name == "Alice")).IsTrue();
			var widget = await promoted.GetAllObjectsAsync().FirstAsync(obj => obj.Name == "Widget");
			var description = await promoted.GetAttributeAsync(widget.DBRef, ["DESCRIBE"]).ToListAsync();
			await Assert.That(description.Last().Value.ToPlainText()).Contains("café 雪");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test]
	[Arguments("legacy", "LEGACY_QDBM", RhostBackend.LegacyQdbm, ".db")]
	[Arguments("mdbx", "MDBX", RhostBackend.Mdbx, ".mdbx")]
	public async Task RealUpstreamExporterRefusesTruncatedNativeStore(string backend, string environmentSuffix,
		RhostBackend kind, string backingSuffix)
	{
		var exporter = Environment.GetEnvironmentVariable("RHOST_EXPORTER_" + environmentSuffix);
		var profile = Environment.GetEnvironmentVariable("RHOST_PROFILE_" + environmentSuffix);
		if (string.IsNullOrWhiteSpace(exporter) || string.IsNullOrWhiteSpace(profile))
		{
			Skip.Test($"Set RHOST_EXPORTER_{environmentSuffix} and RHOST_PROFILE_{environmentSuffix} to run the real pinned upstream converter.");
			return;
		}

		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Join(AppContext.BaseDirectory, "work");
		var root = Path.Join(scratch, "rhost-truncated-" + Guid.NewGuid().ToString("N"));
		var source = Path.Join(root, "source");
		var output = Path.Join(root, "export");
		Directory.CreateDirectory(source);
		try
		{
			foreach (var archive in Directory.EnumerateFiles(Path.Join(FixtureRoot, backend), "*.gz"))
			{
				await using var compressed = File.OpenRead(archive);
				await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
				await using var destination = File.Create(Path.Join(source, Path.GetFileNameWithoutExtension(archive)));
				await gzip.CopyToAsync(destination);
			}
			// The intact checkpoint still has its graph. Only this disposable native backing store is damaged.
			await using (var backing = File.OpenWrite(Path.Join(source, "netrhost.gdbm" + backingSuffix)))
				backing.SetLength(0);
			var before = await SourceHashesAsync(source);
			Exception? failure = null;
			try
			{
				await new RhostOfflineExporter().ExportAsync(new RhostExportOptions
				{
					SourceDirectory = source,
					DatabaseBase = "netrhost.gdbm",
					Checkpoint = "netrhost.db",
					ExporterPath = exporter,
					ExporterProfilePath = profile,
					OutputDirectory = output,
					Backend = kind
				});
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			await Assert.That(failure is InvalidDataException or IOException).IsTrue();
			await Assert.That(Directory.Exists(output)).IsFalse();
			await Assert.That(await SourceHashesAsync(source)).IsEqualTo(before);
			await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo(new[] { source });
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private static LightningDatabase OpenPromoted(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static string CanonicalModel(RhostFlatfile flatfile)
		=> JsonSerializer.Serialize(new
		{
			flatfile.Version,
			flatfile.DatabaseTop,
			flatfile.NextAttribute,
			UserAttributes = flatfile.UserAttributes.OrderBy(x => x.Key).Select(x => new { x.Key, x.Value }),
			Objects = flatfile.Objects.OrderBy(x => x.DBRef).Select(x => x with
			{
				Attributes = x.Attributes.OrderBy(a => a.Number).ThenBy(a => a.Name, StringComparer.Ordinal).ToArray()
			})
		});

	private static async Task<string> SourceHashesAsync(string source)
	{
		var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (var file in Directory.EnumerateFiles(source))
			hashes.Add(Path.GetFileName(file), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file))));
		return JsonSerializer.Serialize(hashes);
	}

	private static async Task AssertHashAsync(string path, string expected)
		=> await Assert.That(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)))).IsEqualTo(expected);

	private static async Task AssertImportedWorldAsync(string flatfile, string? mail = null, string? folders = null)
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(new DatabaseImportFiles(flatfile,
			mail ?? RhostFlatfileReaderTests.MailPath, null, folders ?? RhostFlatfileReaderTests.FolderPath)
		{ Format = DatabaseImportFormat.RhostMUSH }, new Progress<ConversionProgress>());
		await Assert.That(result.Errors).IsEmpty();
		var wizard = (await FindAsync(world, "Wizard")).Expect<SharpPlayer>();
		await Assert.That(world.Passwords.PasswordIsValid("potrzebie", wizard.PasswordHash)).IsTrue();
		var alice = await FindAsync(world, "Alice");
		await Assert.That((await alice.Object().Grants.WithCancellation(CancellationToken.None)).HoldsRole(BuiltInRoles.RoyaltySlug)).IsTrue();
		var widget = await FindAsync(world, "Widget");
		var gadget = await FindAsync(world, "Gadget");
		await Assert.That(widget.Object().Locks["Basic"].LockString).IsEqualTo("(#3)|(#1)");
		await Assert.That((await gadget.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(widget.Object().Key);
		var description = await world.Database.GetAttributeAsync(widget.Object().DBRef, ["DESCRIBE"]).ToListAsync();
		await Assert.That(description.Last().Value.ToPlainText()).Contains("café 雪").And.Contains("[ansi(b,Blue text)]");
		var bob = (await FindAsync(world, "Bob")).Expect<SharpPlayer>();
		var mailbox = await world.Mediator.CreateStream(new GetAllMailListQuery(bob)).ToListAsync();
		await Assert.That(mailbox.Count).IsEqualTo(2);
		await Assert.That(mailbox.Select(x => x.Folder)).Contains("ARCHIVE").And.Contains("INBOX");
	}

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().FirstAsync(x => x.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
