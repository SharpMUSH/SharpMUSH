using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Models;
using SurrealDb.Embedded.RocksDb;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database.SurrealDB;

/// <summary>
/// World backup for the SurrealDB provider. The properties that matter are that the export actually
/// restores — a backup nobody has restored is not a backup — and that it comes back carrying the
/// same data.
///
/// <para>Staging, retention and the <c>latest</c> pointer are <c>WorldBackupWriter</c>'s and are
/// covered once, against Lightning, rather than re-tested per provider.</para>
/// </summary>
public class BackupTests
{
	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-surreal-" + Guid.NewGuid().ToString("N"));

	private static void Delete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: an embedded engine's files can outlive the client's dispose by a moment.
		}
	}

	/// <summary>An embedded RocksDB client on its own directory, which is what production runs.</summary>
	private static ServiceProvider OpenWorld(string directory)
	{
		Directory.CreateDirectory(directory);
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=rocksdb://{directory};Namespace=sharpmush;Database=world").AddRocksDbProvider();
		return services.BuildServiceProvider();
	}

	[Test]
	public async Task AnExportRestoresIntoAFreshWorldCarryingTheSameData()
	{
		var world = TempPath();
		var restored = TempPath();
		var root = TempPath();
		var source = OpenWorld(world);
		try
		{
			var client = source.GetRequiredService<ISurrealDbClient>();
			await client.Connect();
			await client.RawQuery(
				"DEFINE TABLE thing SCHEMALESS; CREATE thing:1 SET name = 'God'; CREATE thing:2 SET name = 'Room Zero';");

			var backups = new SurrealWorldBackupService(client,
				new WorldBackupOptions { Root = root }, NullLogger<SurrealWorldBackupService>.Instance);

			var result = await backups.CreateAsync();

			await Assert.That(result.IsT0).IsTrue();
			var script = await File.ReadAllTextAsync(
				Path.Combine(result.AsT0.Path, SurrealWorldBackupService.ExportFileName));
			await Assert.That(script).IsNotEmpty();

			// The restore: a brand-new world that has never seen this data.
			var target = OpenWorld(restored);
			try
			{
				var restoredClient = target.GetRequiredService<ISurrealDbClient>();
				await restoredClient.Connect();
				await restoredClient.Import(script);

				var names = await restoredClient.Select<ThingRecord>("thing");
				await Assert.That(names.Select(t => t.Name).OrderBy(n => n).ToArray())
					.IsEquivalentTo(new[] { "God", "Room Zero" });
			}
			finally
			{
				await target.DisposeAsync();
			}
		}
		finally
		{
			await source.DisposeAsync();
			Delete(world);
			Delete(restored);
			Delete(root);
		}
	}

	/// <summary>
	/// The export is taken from the world as it stands. A backup that silently missed the most recent
	/// writes would restore to a world that looks fine and is quietly stale.
	/// </summary>
	[Test]
	public async Task AnExportCarriesWritesMadeBeforeIt()
	{
		var world = TempPath();
		var root = TempPath();
		var source = OpenWorld(world);
		try
		{
			var client = source.GetRequiredService<ISurrealDbClient>();
			await client.Connect();
			await client.RawQuery("DEFINE TABLE thing SCHEMALESS; CREATE thing:1 SET name = 'before';");

			var backups = new SurrealWorldBackupService(client,
				new WorldBackupOptions { Root = root }, NullLogger<SurrealWorldBackupService>.Instance);
			var result = await backups.CreateAsync();

			await Assert.That(result.IsT0).IsTrue();
			var script = await File.ReadAllTextAsync(
				Path.Combine(result.AsT0.Path, SurrealWorldBackupService.ExportFileName));
			await Assert.That(script).Contains("before");
		}
		finally
		{
			await source.DisposeAsync();
			Delete(world);
			Delete(root);
		}
	}

	[Test]
	public async Task TheProviderReportsBackupsAsSupported()
	{
		var root = TempPath();
		var world = TempPath();
		var source = OpenWorld(world);
		try
		{
			var backups = new SurrealWorldBackupService(source.GetRequiredService<ISurrealDbClient>(),
				new WorldBackupOptions { Root = root, Keep = 3 }, NullLogger<SurrealWorldBackupService>.Instance);

			await Assert.That(backups.IsSupported).IsTrue();
			await Assert.That(backups.UnavailableReason).IsEmpty();
			await Assert.That(backups.Keep).IsEqualTo(3);
			await Assert.That(backups.Root).IsEqualTo(root);
		}
		finally
		{
			await source.DisposeAsync();
			Delete(world);
			Delete(root);
		}
	}

	private sealed record ThingRecord
	{
		// SurrealDb.Net ignores [JsonPropertyName]; the property must match the field name's casing.
		public string name { get; init; } = string.Empty;

		public string Name => name;
	}
}
