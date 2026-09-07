using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class SyncModeTests
{
	private readonly List<string> _paths = [];

	private string NewPath()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	private static LightningStore Open(string path, LightningSyncMode sync, TimeSpan? flushInterval = null) =>
		new(new LightningStoreOptions
		{
			Path = path,
			MapSize = 256L << 20,
			Sync = sync,
			FlushInterval = flushInterval ?? TimeSpan.FromSeconds(1)
		});

	[After(Test)]
	public Task Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists))
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

	[Test]
	[Arguments(LightningSyncMode.Full)]
	[Arguments(LightningSyncMode.NoMetaSync)]
	[Arguments(LightningSyncMode.Periodic)]
	public async Task DataSurvivesACleanCloseAndReopenInEveryMode(LightningSyncMode mode)
	{
		var path = NewPath();
		using (var store = Open(path, mode))
		{
			await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("k"), Keys.Str("v")));
		}

		using var reopened = Open(path, mode);
		var found = reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("k"), out var v) ? Keys.ReadStr(v) : null);
		await Assert.That(found).IsEqualTo("v");
	}

	[Test]
	public async Task PeriodicModeFlushesOnceAfterAWriteThenStaysIdle()
	{
		using var store = Open(NewPath(), LightningSyncMode.Periodic, TimeSpan.FromMilliseconds(50));
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("k"), Keys.Str("v")));

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (store.FlushCount == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
		await Assert.That(store.FlushCount).IsEqualTo(1);

		// Nothing else was committed, so the timer has nothing to flush.
		await Task.Delay(300);
		await Assert.That(store.FlushCount).IsEqualTo(1);

		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("k2"), Keys.Str("v")));
		deadline = DateTime.UtcNow.AddSeconds(5);
		while (store.FlushCount < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
		await Assert.That(store.FlushCount).IsEqualTo(2);
	}

	[Test]
	[Arguments(LightningSyncMode.Full)]
	[Arguments(LightningSyncMode.NoMetaSync)]
	public async Task OnlyPeriodicModeRunsTheFlushTimer(LightningSyncMode mode)
	{
		using var store = Open(NewPath(), mode, TimeSpan.FromMilliseconds(20));
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("k"), Keys.Str("v")));
		await Task.Delay(200);
		await Assert.That(store.FlushCount).IsEqualTo(0);
	}

	/// <summary>A raw job (a plugin opening its table) commits its own transaction, so the store cannot count
	/// it — but its pages are as unflushed as any other's, and the timer must still sync them.</summary>
	[Test]
	public async Task ARawCommitIsFlushedByThePeriodicTimerToo()
	{
		using var store = Open(NewPath(), LightningSyncMode.Periodic, TimeSpan.FromMilliseconds(50));
		store.OpenTable("plugin_raw", duplicates: false);

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (store.FlushCount == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
		await Assert.That(store.FlushCount).IsEqualTo(1);
	}

	/// <summary>Bad options are rejected before the environment is opened: a throw after the open would
	/// leave the map and lock file held with no store to dispose them. The directory not existing is the
	/// observable — Open is what creates it.</summary>
	[Test]
	[Arguments(0, LightningSyncMode.Full, 1000)]
	[Arguments(64, LightningSyncMode.Periodic, 0)]
	public async Task InvalidOptionsAreRejectedBeforeTheEnvironmentIsOpened(int maxBatch, LightningSyncMode sync, int flushMs)
	{
		var path = NewPath();
		await Assert.That(() => new LightningStore(new LightningStoreOptions
		{
			Path = path,
			MapSize = 256L << 20,
			MaxBatch = maxBatch,
			Sync = sync,
			FlushInterval = TimeSpan.FromMilliseconds(flushMs)
		})).Throws<ArgumentOutOfRangeException>();
		await Assert.That(Directory.Exists(path)).IsFalse();
	}

	[Test]
	[Arguments("full", LightningSyncMode.Full)]
	[Arguments("FULL", LightningSyncMode.Full)]
	[Arguments("nometasync", LightningSyncMode.NoMetaSync)]
	[Arguments(" periodic ", LightningSyncMode.Periodic)]
	public async Task SyncModeParsesCaseInsensitively(string setting, LightningSyncMode expected)
	{
		await Assert.That(LightningStoreOptions.TryParseSyncMode(setting, out var mode)).IsTrue();
		await Assert.That(mode).IsEqualTo(expected);
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("async")]
	public async Task UnknownSyncModeDoesNotParse(string? setting)
	{
		await Assert.That(LightningStoreOptions.TryParseSyncMode(setting, out _)).IsFalse();
	}
}
