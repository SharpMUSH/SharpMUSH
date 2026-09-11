using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
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

	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static long? DbrefNamed(LightningDatabase db, string name)
		=> db.Store.Read(tx => tx.Dups(Tables.ObjName, Keys.Lower(name)).Select(v => (long?)Keys.ReadDbref(v)).FirstOrDefault());

	private static long NextDbref(LightningDatabase db)
		=> db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : -1);

	private static async Task<SharpPlayer> GodAsync(ISharpDatabase db)
		=> (await db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private static async Task Cleanup(LightningDatabase db, string path)
	{
		// Idempotent: a promoted staging database closed its own store already.
		await db.DisposeAsync();

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
			await Cleanup(live, path);
		}
	}

	/// <summary>
	/// Objects are not the only thing a world allocates ids for. Promotion inherits the staged world's
	/// <c>Meta</c> rows along with its data, so a staging database built through the provider needs no
	/// help — but an importer that bulk-loads rows and never maintains the allocators leaves counters
	/// behind their own data, which is exactly what recomputing them on promotion is for. Here the staged
	/// counters are reset to 0 under rows that already exist: unless every allocator is recomputed, the
	/// live world's next account and next wiki page silently overwrite imported ones (neither write
	/// checks whether the key is free).
	/// </summary>
	[Test]
	public async Task PromoteRecomputesEveryCounterOverStagedData()
	{
		var path = TempPath();
		var live = Create(path);
		try
		{
			await live.Migrate();
			var staging = (LightningStagingDatabase)await live.CreateStagingAsync();
			var stagedAccount = await staging.CreateAccountAsync("staged", "staged@example.com", "hash");
			var stagedPage = (await staging.CreateAsync("Staged Page", "body", "#1")).Expect<WikiPage>();
			await staging.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.Meta, Keys.Str("next_account_id"), Keys.Dbref(0));
				tx.Put(Tables.Meta, Keys.Str("next_wiki"), Keys.Dbref(0));
			});

			await staging.PromoteToLiveAsync();

			var laterAccount = await live.CreateAccountAsync("later", "later@example.com", "hash");
			var laterPage = (await live.CreateAsync("Later Page", "body", "#1")).Expect<WikiPage>();

			await Assert.That(laterAccount.Id).IsNotEqualTo(stagedAccount.Id);
			await Assert.That(laterPage.Id).IsNotEqualTo(stagedPage.Id);
			await Assert.That((await live.GetAccountByUsernameAsync("staged"))?.Id).IsEqualTo(stagedAccount.Id);
			await Assert.That((await live.GetByIdAsync(stagedPage.Id)).Expect<WikiPage>().Title).IsEqualTo("Staged Page");
			await Assert.That(live.Store.Count(Tables.Account)).IsEqualTo(2L);
			await Assert.That(live.Store.Count(Tables.WikiPage)).IsEqualTo(2L);
		}
		finally
		{
			await Cleanup(live, path);
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
			await Cleanup(live, path);
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
			await Cleanup(live, path);
		}
	}

	/// <summary>
	/// A read that arrives while the directory is being swapped must block on the store's gate and then
	/// answer from the promoted environment — never fail, and never read a closed one. A holder thread
	/// takes the gate's read lock to make the overlap deterministic: promotion parks waiting for the write
	/// lock, a reader queues behind it, and only when the holder is released does either proceed. All three
	/// participants run on dedicated threads, not the thread pool, so a busy pool cannot be mistaken for a
	/// blocked one — and the holder is a thread rather than this method precisely because
	/// <see cref="ReaderWriterLockSlim"/> is thread-affine: an <c>await</c> between
	/// <c>EnterReadLock</c> and <c>ExitReadLock</c> can resume on another thread, and the exit then throws
	/// while the gate stays held forever.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ReadsIssuedDuringPromotionCompleteAfterIt()
	{
		var path = TempPath();
		var live = Create(path);
		var holding = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		Thread? holder = null;
		Thread? promote = null;
		Thread? read = null;
		try
		{
			await live.Migrate();
			var staging = await live.CreateStagingAsync();
			await staging.CreateRoomAsync("Staged", await GodAsync(staging));

			Exception? holderFailure = null;
			Exception? promoteFailure = null;
			long? readResult = null;

			holder = Run(() =>
			{
				try
				{
					live.Store.Gate.EnterReadLock();
					try
					{
						holding.Set();
						release.Wait();
					}
					finally
					{
						live.Store.Gate.ExitReadLock();
					}
				}
				catch (Exception ex)
				{
					holderFailure = ex;
					holding.Set();
				}
			});
			await WaitFor(() => holding.IsSet, "the holder to take the read lock");

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

			release.Set();

			await Assert.That(holder.Join(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(promote.Join(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(read.Join(TimeSpan.FromSeconds(30))).IsTrue();
			await Assert.That(holderFailure).IsNull();
			await Assert.That(promoteFailure).IsNull();
			await Assert.That(readResult).IsNotNull();
		}
		finally
		{
			// Never delete the directories out from under a participant that is still running.
			release.Set();
			holder?.Join(TimeSpan.FromSeconds(30));
			promote?.Join(TimeSpan.FromSeconds(30));
			read?.Join(TimeSpan.FromSeconds(30));
			holding.Dispose();
			release.Dispose();
			await Cleanup(live, path);
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
