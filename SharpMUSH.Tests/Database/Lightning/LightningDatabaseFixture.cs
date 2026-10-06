using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// A migrated world of its own per test, in a temporary directory, opened directly — no host and no
/// Mediator cache. <see cref="ReopenAsync"/> closes and reopens the same directory, as a restart would.
/// </summary>
public abstract class LightningDatabaseFixture
{
	protected string DbPath { get; private set; } = null!;
	protected LightningDatabase Db { get; private set; } = null!;

	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	[Before(Test)]
	public async Task OpenWorld()
	{
		DbPath = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		Db = Create(DbPath);
		await Db.Migrate();
	}

	[After(Test)]
	public async Task CloseWorld()
	{
		await Db.DisposeAsync();
		if (Directory.Exists(DbPath))
		{
			try
			{
				Directory.Delete(DbPath, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, as in MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	protected async Task ReopenAsync()
	{
		await Db.DisposeAsync();
		Db = Create(DbPath);
		await Db.Migrate();
	}

	protected string[] Dump(TableDef table) => Db.Store.Read(tx => tx.Range(table, [])
		.Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value))
		.ToArray());
}
