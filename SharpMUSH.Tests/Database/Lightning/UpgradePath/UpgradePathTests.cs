using System.IO.Compression;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning.UpgradePath;

/// <summary>
/// Opens every world kept in <c>UpgradePath/Worlds</c> with this build: each was written by the release whose
/// world format its name carries, holding <see cref="UpgradeWorldContents"/>. This build has to migrate it,
/// stamp it with its own format and read back everything the release stored, and do it again after a restart.
/// </summary>
public class UpgradePathTests
{
	internal const string WorldsFolder = "UpgradeWorlds";
	private const string FilePrefix = "format-";
	private const string FileSuffix = ".mdb.gz";

	/// <summary>The kept worlds, by file name: <c>format-&lt;n&gt;.mdb.gz</c>, a gzipped LMDB <c>data.mdb</c>.</summary>
	public static IEnumerable<string> Worlds()
		=> Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, WorldsFolder), FilePrefix + "*" + FileSuffix)
			.Select(path => Path.GetFileName(path))
			.Order(StringComparer.Ordinal);

	internal static string FileName(int format) => FilePrefix + format + FileSuffix;

	private static int FormatOf(string file) => int.Parse(file[FilePrefix.Length..^FileSuffix.Length]);

	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	internal static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static string NewPath() => Path.Join(Path.GetTempPath(), "upgrade-path-" + Guid.NewGuid().ToString("N"));

	private static string Unpack(string file)
	{
		var path = NewPath();
		Directory.CreateDirectory(path);
		using var source = new GZipStream(File.OpenRead(Path.Join(AppContext.BaseDirectory, WorldsFolder, file)), CompressionMode.Decompress);
		using var target = File.Create(Path.Join(path, "data.mdb"));
		source.CopyTo(target);
		return path;
	}

	[Test]
	public async Task TheFirstReleasedFormatIsKept()
	{
		var formats = Worlds().Select(FormatOf).ToArray();

		await Assert.That(formats).Contains(1)
			.Because("1.0.0 writes format 1, and every later server has to open a 1.0.0 world");
		await Assert.That(formats.All(format => format <= LightningDatabase.WorldFormat)).IsTrue()
			.Because("a kept world is written by a released server, never one newer than this build");
	}

	[Test]
	[MethodDataSource(nameof(Worlds))]
	public async Task AKeptWorldIsStampedWithTheFormatItsNameCarries(string file)
	{
		var path = Unpack(file);
		var db = Create(path);
		try
		{
			await Assert.That(db.Store.Read(LightningDatabase.ReadWorldFormat)).IsEqualTo(FormatOf(file));
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	[Test]
	[MethodDataSource(nameof(Worlds))]
	public async Task AKeptWorldOpensWithThisBuildAndKeepsWhatItHeld(string file)
	{
		var path = Unpack(file);
		var db = Create(path);
		try
		{
			await db.Migrate();

			await Assert.That(db.Store.Read(LightningDatabase.ReadWorldFormat)).IsEqualTo(LightningDatabase.WorldFormat);
			await Assert.That(await db.GetObjectCountAsync()).IsEqualTo(UpgradeWorldContents.ObjectCount)
				.Because("a migration neither adds nor drops objects");
			await UpgradeWorldContents.VerifyAsync(db);

			await db.DisposeAsync();
			db = Create(path);
			await db.Migrate();

			await UpgradeWorldContents.VerifyAsync(db);
			await Assert.That(await db.GetNextDbrefAsync()).IsEqualTo(UpgradeWorldContents.ObjectCount)
				.Because("the next object after a restart is the one after the world's last");
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}
}

/// <summary>
/// Writes this build's world into <c>UpgradePath/Worlds</c> as <c>format-&lt;WorldFormat&gt;.mdb.gz</c>. Run it on the
/// release that first writes a format, and commit the file:
/// <c>dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/UpgradeWorldFixtureWriter/*"</c>.
/// It refuses to replace a kept world unless <c>SHARPMUSH_UPGRADE_WORLD_REPLACE=true</c>.
/// </summary>
[Explicit]
public class UpgradeWorldFixtureWriter
{
	[Test]
	public async Task WriteThisBuildsWorld() => await WriteAsync();

	private static async Task WriteAsync([CallerFilePath] string source = "")
	{
		var target = Path.Join(Path.GetDirectoryName(source)!, "Worlds", UpgradePathTests.FileName(LightningDatabase.WorldFormat));
		if (File.Exists(target) && Environment.GetEnvironmentVariable("SHARPMUSH_UPGRADE_WORLD_REPLACE") != "true")
			throw new InvalidOperationException($"{target} is kept from the release that wrote it; set SHARPMUSH_UPGRADE_WORLD_REPLACE=true to replace it.");

		var path = Path.Join(Path.GetTempPath(), "upgrade-write-" + Guid.NewGuid().ToString("N"));
		var copy = path + ".copy";
		var db = UpgradePathTests.Create(path);
		try
		{
			await db.Migrate();
			await UpgradeWorldContents.WriteAsync(db);
			await UpgradeWorldContents.VerifyAsync(db);
			db.Store.CopyTo(copy, compact: true);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			await using var input = File.OpenRead(Path.Join(copy, "data.mdb"));
			await using var output = new GZipStream(File.Create(target), CompressionLevel.SmallestSize);
			await input.CopyToAsync(output);
		}
		finally
		{
			await FixtureDirectoryCleanup.DeleteAsync(copy);
		}
	}
}
