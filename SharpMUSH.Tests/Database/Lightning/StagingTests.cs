using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Staging promotion swaps the live directory beneath a running provider. These tests drive the
/// provider directly (no host, no DI) so the swap is observed exactly where it happens: the live
/// store's own gate.
/// </summary>
public class StagingTests
{
	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>());

	private static long? DbrefNamed(LightningDatabase db, string name)
		=> db.Store.Read(tx => tx.Dups(Tables.ObjName, Keys.Lower(name)).Select(v => (long?)Keys.ReadDbref(v)).FirstOrDefault());

	private static long NextDbref(LightningDatabase db)
		=> db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : -1);

	private static async Task<SharpPlayer> GodAsync(ISharpDatabase db)
		=> (await db.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

	private static void Cleanup(LightningDatabase db, string path)
	{
		try
		{
			db.Store.Dispose();
		}
		catch (ObjectDisposedException)
		{
			// A promoted staging database disposed its own store already.
		}

		foreach (var directory in new[] { path, path + ".previous" })
		{
			if (!Directory.Exists(directory)) continue;
			try
			{
				Directory.Delete(directory, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
			}
		}
	}

	[Test]
	public async Task PromoteMakesStagedDataLiveAndMovesTheCounter()
	{
		var path = TempPath();
		var live = Create(path);
		try
		{
			await live.Migrate();
			await live.CreateRoomAsync("LiveOnly", await GodAsync(live));

			var staging = await live.CreateStagingAsync();
			var stagingPath = ((LightningStagingDatabase)staging).StagingPath;
			await staging.CreateRoomAsync("Staged", await GodAsync(staging));
			var stagedCounter = NextDbref((LightningDatabase)staging);

			await staging.PromoteToLiveAsync();

			await Assert.That(staging.IsPromoted).IsTrue();
			await Assert.That(DbrefNamed(live, "Staged")).IsNotNull();
			await Assert.That(DbrefNamed(live, "LiveOnly")).IsNull();
			await Assert.That(NextDbref(live)).IsEqualTo(stagedCounter);
			await Assert.That(Directory.Exists(stagingPath)).IsFalse();
			await Assert.That(Directory.Exists(path + ".previous")).IsTrue();

			// The live provider keeps working on the swapped-in environment.
			var after = await live.CreateRoomAsync("AfterPromote", await GodAsync(live));
			await Assert.That(after.Number).IsEqualTo((int)stagedCounter);
		}
		finally
		{
			Cleanup(live, path);
		}
	}

	[Test]
	public async Task AbortLeavesLiveUntouchedAndRemovesTheStagingDirectory()
	{
		var path = TempPath();
		var live = Create(path);
		try
		{
			await live.Migrate();
			await live.CreateRoomAsync("LiveOnly", await GodAsync(live));
			var counterBefore = NextDbref(live);

			var staging = await live.CreateStagingAsync();
			var stagingPath = ((LightningStagingDatabase)staging).StagingPath;
			await staging.CreateRoomAsync("Staged", await GodAsync(staging));
			await Assert.That(Directory.Exists(stagingPath)).IsTrue();

			await staging.AbortAsync();

			await Assert.That(staging.IsPromoted).IsFalse();
			await Assert.That(Directory.Exists(stagingPath)).IsFalse();
			await Assert.That(DbrefNamed(live, "LiveOnly")).IsNotNull();
			await Assert.That(DbrefNamed(live, "Staged")).IsNull();
			await Assert.That(NextDbref(live)).IsEqualTo(counterBefore);
		}
		finally
		{
			Cleanup(live, path);
		}
	}

	[Test]
	public async Task DisposeWithoutPromoteOrAbortCleansUpTheStagingDirectory()
	{
		var path = TempPath();
		var live = Create(path);
		try
		{
			await live.Migrate();
			string stagingPath;
			await using (var staging = await live.CreateStagingAsync())
			{
				stagingPath = ((LightningStagingDatabase)staging).StagingPath;
				await staging.CreateRoomAsync("Staged", await GodAsync(staging));
			}

			await Assert.That(Directory.Exists(stagingPath)).IsFalse();
			await Assert.That(DbrefNamed(live, "Staged")).IsNull();
		}
		finally
		{
			Cleanup(live, path);
		}
	}

	/// <summary>
	/// A read that arrives while the directory is being swapped must block on the store's gate and then
	/// answer from the promoted environment — never fail, and never read a closed one. The test holds the
	/// gate itself to make the overlap deterministic: promotion parks waiting for the write lock, a reader
	/// queues behind it, and only when the test releases its own read lock does either proceed. Both run on
	/// dedicated threads, not the thread pool, so a busy pool cannot be mistaken for a blocked participant.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ReadsIssuedDuringPromotionCompleteAfterIt()
	{
		var path = TempPath();
		var live = Create(path);
		Thread? promote = null;
		Thread? read = null;
		try
		{
			await live.Migrate();
			var staging = await live.CreateStagingAsync();
			await staging.CreateRoomAsync("Staged", await GodAsync(staging));

			Exception? promoteFailure = null;
			long? readResult = null;

			live.Store.Gate.EnterReadLock();
			try
			{
				promote = Run(() =>
				{
					try
					{
						staging.PromoteToLiveAsync().GetAwaiter().GetResult();
					}
					catch (Exception ex)
					{
						promoteFailure = ex;
					}
				});
				await WaitFor(() => live.Store.Gate.WaitingWriteCount > 0, "promotion to reach the write lock");

				read = Run(() => readResult = DbrefNamed(live, "Staged"));
				await WaitFor(() => live.Store.Gate.WaitingReadCount > 0, "the read to queue behind the swap");

				await Assert.That(promote.IsAlive).IsTrue();
				await Assert.That(read.IsAlive).IsTrue();
			}
			finally
			{
				live.Store.Gate.ExitReadLock();
			}

			await Assert.That(promote.Join(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(read.Join(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(promoteFailure).IsNull();
			await Assert.That(readResult).IsNotNull();
		}
		finally
		{
			// Never delete the directories out from under a participant that is still running.
			promote?.Join(TimeSpan.FromSeconds(30));
			read?.Join(TimeSpan.FromSeconds(30));
			Cleanup(live, path);
		}
	}

	private static Thread Run(Action work)
	{
		var thread = new Thread(() => work()) { IsBackground = true };
		thread.Start();
		return thread;
	}

	private static async Task WaitFor(Func<bool> condition, string what)
	{
		var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
		while (!condition())
		{
			if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
			await Task.Delay(10);
		}
	}
}
