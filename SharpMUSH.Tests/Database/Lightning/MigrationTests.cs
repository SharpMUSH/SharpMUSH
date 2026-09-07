using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class MigrationTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	[Test]
	public async Task MigrateSeedsTheWorldOnceAndIsIdempotent()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();
			await db.Migrate();

			await Assert.That(db.Store.Count(Tables.Obj)).IsEqualTo(10);
			await Assert.That(db.Store.Count(Tables.Flag)).IsEqualTo(61);
			await Assert.That(db.Store.Count(Tables.AttrEntry)).IsEqualTo(216);

			var next = db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : -1);
			await Assert.That(next).IsEqualTo(10);

			var godName = db.Store.Read(tx => tx.TryGet(Tables.Obj, Keys.Dbref(1), out var v)
				? Codec.Deserialize<ObjectRecord>(v).Name
				: null);
			await Assert.That(godName).IsEqualTo("God");
		}
		finally
		{
			await db.DisposeAsync();
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch (IOException)
				{
					// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
					// join by a few milliseconds under load. Leaving the temp directory behind costs
					// disk, not correctness — matches ServerWebAppFactory's own cleanup.
				}
			}
		}
	}
}
