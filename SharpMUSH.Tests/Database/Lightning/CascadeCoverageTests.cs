using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The catalogue is the delete cascade's only map of the world: an edge with no pair would leave a
/// dangling reverse row on delete, and a sub-database on disk that <see cref="Tables.All"/> does not
/// name would never be opened, cascaded or swapped. Both are checked against what LMDB itself reports.
/// </summary>
public class CascadeCoverageTests
{
	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static LightningStoreOptions Options(string path) => new() { Path = path, MapSize = 256L << 20 };

	private static void Delete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
		}
	}

	/// <summary>
	/// Every sub-database name LMDB holds, read from the unnamed root database — the environment's own
	/// index of named sub-databases. Opened after the store is disposed: LMDB permits one environment
	/// per path per process.
	/// </summary>
	private static string[] OnDiskNames(LightningStoreOptions options)
	{
		using var env = new LightningEnvironment(options.Path, new EnvironmentConfiguration
		{
			MapSize = options.MapSize,
			MaxDatabases = options.MaxDatabases,
			MaxReaders = options.MaxReaders,
			PageSize = options.PageSize
		});
		env.Open(EnvironmentOpenFlags.NoThreadLocalStorage | EnvironmentOpenFlags.ReadOnly);

		using var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);
		using var root = tx.OpenDatabase();
		using var cursor = tx.CreateCursor(root);
		var names = new List<string>();
		if (cursor.First().resultCode != MDBResultCode.Success) return [];
		do
		{
			var (code, key, _) = cursor.GetCurrent();
			if (code != MDBResultCode.Success) break;
			// LightningDB marshals sub-database names NUL-terminated, and LMDB stores the terminator
			// as part of the key.
			names.Add(Keys.ReadStr(key.CopyToNewArray()).TrimEnd('\0'));
		} while (cursor.Next().resultCode == MDBResultCode.Success);

		return [.. names];
	}

	[Test]
	public async Task EveryForwardEdgeHasAReverseAndEveryReverseHasAForward()
	{
		var forward = Tables.All.Where(t => t.Kind == TableKind.ForwardEdge).ToArray();
		var reverse = Tables.All.Where(t => t.Kind == TableKind.ReverseEdge).ToArray();

		await Assert.That(forward.Length).IsGreaterThan(0);
		await Assert.That(forward.Length).IsEqualTo(reverse.Length);

		foreach (var edge in forward)
		{
			await Assert.That(edge.Pair).IsNotNull();
			await Assert.That(edge.Pair!.Kind).IsEqualTo(TableKind.ReverseEdge);
			await Assert.That(edge.Pair.Pair).IsEqualTo(edge);
			await Assert.That(edge.Pair.Duplicates).IsEqualTo(edge.Duplicates);
			await Assert.That(edge.Pair.FixedDuplicates).IsEqualTo(edge.FixedDuplicates);
		}

		foreach (var edge in reverse)
		{
			await Assert.That(edge.Pair).IsNotNull();
			await Assert.That(edge.Pair!.Kind).IsEqualTo(TableKind.ForwardEdge);
			await Assert.That(edge.Pair.Pair).IsEqualTo(edge);
		}

		var cascaded = Tables.EdgePairs.ToArray();
		await Assert.That(cascaded.Length).IsEqualTo(forward.Length);
		await Assert.That(cascaded.Select(p => p.Reverse.Name).Order().ToArray())
			.IsEquivalentTo(reverse.Select(t => t.Name).Order().ToArray());
	}

	[Test]
	public async Task AFreshStoreHoldsExactlyTheCataloguedTables()
	{
		var path = TempPath();
		var options = Options(path);
		try
		{
			using (var store = new LightningStore(options))
			{
				await Assert.That(store.Path).IsEqualTo(path);
			}

			var onDisk = OnDiskNames(options).Order().ToArray();
			await Assert.That(onDisk).IsEquivalentTo(Tables.All.Select(t => t.Name).Order().ToArray());
		}
		finally
		{
			Delete(path);
		}
	}

	[Test]
	public async Task APluginOpenedTableIsTheOnlyNameBeyondTheCatalogue()
	{
		var path = TempPath();
		var options = Options(path);
		try
		{
			using (var store = new LightningStore(options))
			{
				store.OpenTable("scene.test", duplicates: true);
			}

			var onDisk = OnDiskNames(options);
			var extra = onDisk.Except(Tables.All.Select(t => t.Name)).ToArray();

			await Assert.That(onDisk).Contains("scene.test");
			await Assert.That(extra).IsEquivalentTo(new[] { "scene.test" });
			await Assert.That(extra.All(n => n.StartsWith("scene", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Delete(path);
		}
	}

	/// <summary>A plugin table opened before a directory swap must still be open after it — the store
	/// reopens the plugin names it was handed, not just its own catalogue.</summary>
	[Test]
	public async Task APluginOpenedTableSurvivesADirectorySwap()
	{
		var path = TempPath();
		var incoming = TempPath();
		var previous = path + ".previous";
		var options = Options(path);
		try
		{
			using (var seed = new LightningStore(Options(incoming)))
			{
				await seed.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("incoming"), Keys.Str("1")));
			}

			using var store = new LightningStore(options);
			var pluginTable = store.OpenTable("scene.test", duplicates: true);
			await store.WriteAsync(tx => tx.Put(pluginTable, Keys.Dbref(1), Keys.Dbref(2)));

			store.SwapDirectory(incoming, previous);

			await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("incoming"), out _))).IsTrue();
			// The swapped-in directory never held the plugin table; reopening it recreates it, empty.
			await Assert.That(store.Count(pluginTable)).IsEqualTo(0L);
			await store.WriteAsync(tx => tx.Put(pluginTable, Keys.Dbref(3), Keys.Dbref(4)));
			await Assert.That(store.Count(pluginTable)).IsEqualTo(1L);
			await Assert.That(ReferenceEquals(store.OpenTable("scene.test", duplicates: true), pluginTable)).IsTrue();
		}
		finally
		{
			Delete(path);
			Delete(incoming);
			Delete(previous);
		}
	}
}
