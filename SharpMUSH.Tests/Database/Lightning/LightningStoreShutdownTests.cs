using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Disposal closes the environment only once nothing is inside it. These never unmap an active transaction:
/// a seam runs just before the native close and, should a read still be inside, records that and lets it
/// finish first, so the old behaviour fails the test without the crash it would cause.
/// </summary>
public class LightningStoreShutdownTests
{
	private readonly List<string> _paths = [];

	private LightningStore Open(string? path = null)
	{
		path ??= Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20 });
	}

	[After(Test)]
	public Task Cleanup()
	{
		foreach (var path in _paths.Distinct().Where(Directory.Exists))
		{
			try { Directory.Delete(path, recursive: true); }
			catch (IOException) { }
		}
		return Task.CompletedTask;
	}

	/// <summary>A read held open on another thread until <c>Release</c>.</summary>
	private sealed class HeldRead : IDisposable
	{
		private readonly ManualResetEventSlim _release = new(false);
		internal Task<int> Done { get; }
		internal HeldRead(LightningStore store)
		{
			var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			Done = Task.Run(() => store.Read(_ => { opened.SetResult(); _release.Wait(TimeSpan.FromSeconds(30)); return 1; }));
			opened.Task.Wait(TimeSpan.FromSeconds(30));
		}
		internal void Release() => _release.Set();
		public void Dispose() { _release.Set(); _release.Dispose(); }
	}

	/// <summary>The seam: records any read still inside, then lets it finish so the close never runs under it.</summary>
	private static Func<bool> GuardClose(LightningStore store, HeldRead read)
	{
		var inside = false;
		store.BeforeNativeClose = () =>
		{
			if (store.ReaderStatistics.Active == 0) return;
			inside = true;
			read.Release();
			read.Done.Wait(TimeSpan.FromSeconds(30));
		};
		return () => inside;
	}

	private static async Task WaitForDisposalToWait(LightningStore store)
	{
		for (var i = 0; i < 3000 && !store.DisposalWaitingForReaders; i++) await Task.Delay(10);
	}

	[Test]
	public async Task DisposalWaitsForAnAdmittedReadBeforeClosing()
	{
		var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("shutdown-probe"), [7]));
		using var read = new HeldRead(store);
		var readInside = GuardClose(store, read);
		var disposal = Task.Run(store.Dispose);
		await WaitForDisposalToWait(store);
		await Assert.That(disposal.IsCompleted).IsFalse();
		read.Release();
		await disposal.WaitAsync(TimeSpan.FromSeconds(30));
		await Assert.That(readInside()).IsFalse();
		await Assert.That(await read.Done).IsEqualTo(1);

		// What was committed is still there.
		using var reopened = Open(_paths[0]);
		await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("shutdown-probe"), out var value) && value is [7])).IsTrue();
	}

	[Test]
	public async Task ConcurrentDisposalsJoinTheOneCleanup()
	{
		var store = Open();
		using var read = new HeldRead(store);
		var closes = 0;
		var readInside = false;
		store.BeforeNativeClose = () =>
		{
			Interlocked.Increment(ref closes);
			if (store.ReaderStatistics.Active == 0) return;
			readInside = true;
			read.Release();
			read.Done.Wait(TimeSpan.FromSeconds(30));
		};
		var first = Task.Run(store.Dispose);
		await WaitForDisposalToWait(store);
		var second = Task.Run(store.Dispose);
		await Task.Delay(50);
		// The second waits for the first's cleanup rather than returning before the environment is closed.
		await Assert.That(first.IsCompleted || second.IsCompleted).IsFalse();
		read.Release();
		await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
		await Assert.That(closes).IsEqualTo(1);
		await Assert.That(readInside).IsFalse();
		store.Dispose();
	}

	[Test]
	public async Task EveryNativeEntryIsRefusedAfterDisposal()
	{
		var store = Open();
		store.Dispose();
		var copy = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-copy-" + Guid.NewGuid().ToString("N"));
		_paths.Add(copy);
		foreach (var entry in new Action[]
		{
			() => store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("x"), out _)),
			() => store.CopyTo(copy),
			() => store.Usage(),
			() => store.EntryCounts(),
			() => store.CheckStaleReaders(),
			() => store.TableFootprint(Tables.Meta),
			() => store.PrefixFootprint(Tables.Meta, [])
		})
			await Assert.That(entry).Throws<ObjectDisposedException>();
	}

	[Test]
	public async Task DisposingFromInsideItsOwnReadIsRefusedAndChangesNothing()
	{
		var store = Open();
		await Assert.That(() => store.Read(_ => { store.Dispose(); return 0; })).Throws<InvalidOperationException>();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("still-open"), [1]));
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("still-open"), out _))).IsTrue();
		store.Dispose();
	}
}
