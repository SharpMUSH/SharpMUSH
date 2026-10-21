using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tools.WikiSemantics.Native;

// Disposable native COMMIT experiments. No production diagnostics or fault callback.
internal static class CategoryCommitFailureScenario
{
	private const string Category = "play\0rules";
	private const string Timestamp = "2023-11-14T22:13:20.0000000+00:00";
	private const int ProofCap = 32768;
	private static readonly byte[] PageKey = new byte[8];
	private static readonly byte[] NeighborAKey = "test.category.failed-group/A"u8.ToArray();
	private static readonly byte[] NeighborBKey = "test.category.failed-group/B"u8.ToArray();

	internal static Task RunAsync(string world, string evidence, bool resume, Action<string> mark)
		=> RunCoreAsync(world, evidence, resume, false, mark);

	internal static Task RunGroupedAsync(string world, string evidence, bool resume, Action<string> mark)
		=> RunCoreAsync(world, evidence, resume, true, mark);

	private static async Task RunCoreAsync(string world, string evidence, bool resume, bool grouped, Action<string> mark)
	{
		KernelRestriction.RequireAbi();
		world = Path.GetFullPath(world);
		evidence = Path.GetFullPath(evidence);
		Check(world.Length <= 1024 && evidence.Length <= 1024, "Owned path too long");
		Check(Path.GetDirectoryName(world) == Path.GetDirectoryName(evidence) && world != evidence,
			"World and evidence must be new siblings");
		RequireAbsent(world);
		RequireAbsent(evidence);
		Directory.CreateDirectory(evidence);
		RejectLinkedAncestors(evidence);
		var proofPath = Path.Join(evidence, "category-commit-failure.json");
		RequireAbsent(proofPath);
		// Fixed storage and ordinary sinks exist before the restricted interval.
		var proofBuffer = new byte[ProofCap];
		using var proofStream = new MemoryStream(proofBuffer, 0, proofBuffer.Length, true, true);
		var options = WikiCategoryMigrationOptions.Parse(_ => null);
		var storeOptions = new LightningStoreOptions
		{
			Path = world, MapSize = 256L << 20,
			Sync = LightningSyncMode.Full, ReaderCheckInterval = TimeSpan.Zero, MaxBatch = grouped ? 3 : 64
		};
		var db = Open(storeOptions);
		try
		{
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.WikiPage, PageKey, Codec.Serialize(new WikiPageRecord
				{
					Slug = "guide", Title = "Guide", Namespace = "main", MarkdownSource = "body",
					RenderedHtml = "<p>body</p>", PlainText = "body", AuthorDbref = "#42", LastEditorDbref = "#42",
					CreatedAt = Timestamp, UpdatedAt = Timestamp, RevisionNumber = 1,
					Published = true, SourceLocale = "en", Categories = [Category]
				}));
				tx.Put(Tables.WikiByCategory, new byte[] { 1, 2 }, new byte[] { 1 });
				if (resume) tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey,
					WikiCategoryCheckpointCodec.Encode(new(WikiCategoryMigrationPhase.Clearing, 1, 0, 0, 0,
						[], [], [], [], 0, 0), db.Store.MaxKeySize));
			});
			var provenance = LoadedProvenance();
			var pageSize = checked((ulong)db.Store.Usage().PageSize);
			Check(pageSize > 0, "Invalid actual native page size");
			var armedSoft = checked(2 * pageSize); // Pinned packaged LMDB_1.0.1 NUM_METAS=2.
			var beforeHash = Snapshot(db, grouped);
			var sourceBeforeHash = SourceHash(db);
			await RequireUnready(db);
			var commitsBefore = db.Store.CommitCount;
			using var restriction = new KernelRestriction(armedSoft);
			using var cohort = grouped ? new GroupCohort(db, resume, options) : null;
			// Warm canonical hashing, P/Invoke queries/setters, pipe sinks and proof serializer before arm.
			_ = restriction.BeforeHash;
			using (var warm = new Utf8JsonWriter(proofStream)) { warm.WriteStartObject(); warm.WriteEndObject(); warm.Flush(); }
			proofStream.Position = 0;
			Console.Error.Flush();
			mark("category-seeded");
			LightningStoreException? failure = null;
			if (cohort is not null) failure = await cohort.FailAsync(restriction);
			else
			{
				try
				{
					restriction.Arm();
					try { await db.MigrateWikiCategoryValuesAsync(options); }
					catch (LightningStoreException error) { failure = error; }
				}
				finally
				{
					// Fatal exit on restoration failure deliberately bypasses provider cleanup.
					restriction.RestoreOrExit();
				}
			}
			if (failure is null || (int)failure.Code != 27 || !failure.Message.StartsWith("commit:", StringComparison.Ordinal))
				throw new InvalidDataException("Expected actual native EFBIG at commit");
			// All original producers have settled and native restrictions are restored. Keep actual
			// causal frames, rather than bounding source paths and the shared exception's async tail.
			var commitStack = CategoryCommitStack.Capture(failure.StackTrace ?? "", grouped);
			mark("category-commit-failed");
			mark("category-limits-restored");
			var commitsAfterFailure = db.Store.CommitCount;
			Check(commitsBefore == commitsAfterFailure, "Failed commit count advanced");
			var afterFailureHash = Snapshot(db, grouped);
			Check(beforeHash == afterFailureHash, "Failed commit changed logical state");
			await RequireUnready(db);
			await db.DisposeAsync();
			db = Open(storeOptions);
			var afterReopenHash = Snapshot(db, grouped);
			Check(beforeHash == afterReopenHash, "Reopen changed failed logical state");
			await RequireUnready(db);
			mark("category-reopened");
			var retryBefore = db.Store.CommitCount;
			await db.MigrateWikiCategoryValuesAsync(options);
			var retryCommits = db.Store.CommitCount - retryBefore;
			Check(retryCommits > 0, "Retry did not commit");
			await VerifyComplete(db);
			if (grouped) db.Store.Read(tx => { RequireNeighborsAbsent(tx); return true; });
			var sourceAfterRetryHash = SourceHash(db);
			Check(sourceBeforeHash == sourceAfterRetryHash, "Retry changed raw source bytes");
			mark("category-retry-complete");
			var scenario = (grouped ? "category-group-commit-failure-" : "category-commit-failure-") + (resume ? "resume" : "initial");
			var proof = new
			{
				schema = 1,
				pid = Environment.ProcessId,
				world,
				scenario,
				resumed = resume,
				pageSize,
				armedSoft,
				softBefore = restriction.Before.Soft,
				hardBefore = restriction.Before.Hard,
				softAfter = restriction.After.Soft,
				hardAfter = restriction.After.Hard,
				signalRestored = true,
				signalStateBeforeHash = restriction.BeforeHash,
				signalStateAfterHash = restriction.AfterHash,
				nativeCode = (int)failure.Code,
				operation = "commit",
				commitStack,
				commitsBefore,
				commitsAfterFailure,
				beforeHash,
				afterFailureHash,
				afterReopenHash,
				sourceBeforeHash,
				sourceAfterRetryHash,
				retryCommits,
				nativeLibrary = provenance.Path,
				nativeSha256 = provenance.Hash,
				lightningPackage = "0.23.1",
				lmdbSource = "LMDB_1.0.1",
				metadataPages = 2
			};
			// Admit worst-case JSON escaping before serialization, in addition to the nonexpanding output buffer.
			Check(checked((grouped ? 6144 : 4096) + 6 * (world.Length + scenario.Length + commitStack.Length + provenance.Path.Length + 9 * 64)) <= ProofCap,
				"Proof exceeds admitted fixed output budget");
			using (var writer = new Utf8JsonWriter(proofStream))
			{
				if (cohort is null) JsonSerializer.Serialize(writer, proof);
				else JsonSerializer.Serialize(writer, new
				{
					proof.schema,
					proof.pid,
					proof.world,
					proof.scenario,
					proof.resumed,
					proof.pageSize,
					proof.armedSoft,
					proof.softBefore,
					proof.hardBefore,
					proof.softAfter,
					proof.hardAfter,
					proof.signalRestored,
					proof.signalStateBeforeHash,
					proof.signalStateAfterHash,
					proof.nativeCode,
					proof.operation,
					proof.commitStack,
					proof.commitsBefore,
					proof.commitsAfterFailure,
					proof.beforeHash,
					proof.afterFailureHash,
					proof.afterReopenHash,
					proof.sourceBeforeHash,
					proof.sourceAfterRetryHash,
					proof.retryCommits,
					proof.nativeLibrary,
					proof.nativeSha256,
					proof.lightningPackage,
					proof.lmdbSource,
					proof.metadataPages,
					grouped = true,
					cohortSize = 3,
					maxBatchJobs = 3,
					allQueuedPending = cohort.AllQueuedPending,
					sameCommitException = cohort.SameCommitException,
					neighborACallbacks = cohort.NeighborACallbacks,
					neighborBCallbacks = cohort.NeighborBCallbacks,
					neighborASawCandidate = cohort.NeighborASawCandidate,
					neighborBSawCandidate = cohort.NeighborBSawCandidate,
					neighborBSawA = cohort.NeighborBSawA,
					producers = cohort.Producers()
				});
				writer.Flush();
			}
			var proofLength = checked((int)proofStream.Position);
			Check(proofLength <= ProofCap, "Proof cap exceeded");
			// Close the recovered world before evidence publication and final normal-exit marker.
			await db.DisposeAsync();
			RejectLinkedAncestors(world);
			RequireAbsent(proofPath);
			using (var file = new FileStream(proofPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				file.Write(proofBuffer, 0, proofLength);
			mark("category-commit-failure-complete");
		}
		finally { await db.DisposeAsync(); }
	}

	private static LightningDatabase Open(LightningStoreOptions options)
		=> new(NullLogger<LightningDatabase>.Instance, options, new UnusedPasswordService(), null);

	private static async Task RequireUnready(LightningDatabase db)
	{
		try { await db.CountPagesInCategoryAsync(Category, WikiVisibility.All); }
		catch (InvalidOperationException error) when (error.Message == "Wiki category migration is not ready.") { return; }
		throw new InvalidDataException("Category readiness published before successful migration");
	}

	private static async Task VerifyComplete(LightningDatabase db)
	{
		db.Store.Read(tx =>
		{
			Check(tx.Count(Tables.WikiPage) == 1 && tx.Count(Tables.WikiByCategory) == 1, "Unexpected recovered row count");
			Check(tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, out var state)
				&& tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, out _), "Missing Complete pair");
			tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, out var marker);
			Check(WikiCategoryAuthorityCodec.Validate(true, state, true, marker, db.Store.MaxKeySize), "Invalid Complete pair");
			var checkpoint = WikiCategoryCheckpointCodec.Decode(state, db.Store.MaxKeySize);
			Check(checkpoint.Phase == WikiCategoryMigrationPhase.Complete && checkpoint.ClearedRows == 1
				&& checkpoint.CompletedPages == 1 && checkpoint.CategoryPuts == 1, "Incorrect completion counters");
			var row = tx.Range(Tables.WikiByCategory, []).Single();
			var key = "PLAY\0RULES\0"u8.ToArray().Concat(Encoding.BigEndianUnicode.GetBytes("Guide")).Concat(new byte[10]).ToArray();
			byte[] value = [1, 35, 52, 50, 0, 0, 0, 10, 255, 67, 0, 1];
			Check(row.Key.AsSpan().SequenceEqual(key) && row.Value.AsSpan().SequenceEqual(value), "Incorrect recovered raw category row");
			return true;
		});
		var pages = await db.GetPagesByCategoryAsync(Category, 0, 10, WikiVisibility.All);
		Check(pages.Count == 1, "Incorrect recovered membership");
		var page = pages[0];
		var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000);
		Check(page.Id == "wiki_page/0" && page.Title == "Guide" && page.Slug == "guide" && page.Namespace == "main"
			&& page.MarkdownSource == "body" && page.RenderedHtml == "<p>body</p>" && page.PlainText == "body"
			&& page.AuthorDbref == "#42" && page.LastEditorDbref == "#42" && page.Published
			&& page.CreatedAt == timestamp && page.UpdatedAt == timestamp && page.RevisionNumber == 1
			&& page.SourceLocale == "en" && page.Categories.SequenceEqual(new[] { Category }), "Incorrect recovered metadata");
		Check(await db.CountPagesInCategoryAsync(Category, WikiVisibility.All) == 1
			&& await db.CountPagesInCategoryAsync("play", WikiVisibility.All) == 0, "Incorrect category counts");
		Check((await db.GetPagesByCategoryAsync("play", 0, 10, WikiVisibility.All)).Count == 0, "Incorrect short category alias");
		var counts = await db.CountPagesByCategoryAsync(WikiVisibility.All);
		Check(counts.Count == 1 && counts.TryGetValue(Category, out var count) && count == 1, "Incorrect category dictionary");
	}

	// Fixed table order, table-name length/name, row marker, key length/key, value length/value,
	// terminal marker per table. Authority keys are present even when their values are absent.
	private static string Snapshot(LightningDatabase db, bool grouped = false) => db.Store.Read(tx =>
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var budget = new NativeReadBudget(64, ProofCap, 4 * ProofCap, default);
		foreach (var table in new[] { Tables.WikiPage, Tables.WikiByCategory })
		{
			Frame(hash, Encoding.UTF8.GetBytes(table.Name));
			using var rows = ((IBoundedReadTx)tx).ReadRange(table, [], [], [], [], budget);
			Check(rows.Exhausted && rows.Entries.Length <= 1, "Unexpected bounded fixture snapshot");
			foreach (var row in rows.Entries) { hash.AppendData(new byte[] { 1 }); Frame(hash, row.Key); Frame(hash, row.Value); }
			hash.AppendData(new byte[] { 0 });
		}
		Frame(hash, Encoding.UTF8.GetBytes(Tables.Meta.Name));
		var metaKeys = grouped
			? new[] { WikiCategoryCheckpointCodec.StateKey.ToArray(), WikiCategoryCheckpointCodec.CompletionKey.ToArray(), NeighborAKey, NeighborBKey }
			: new[] { WikiCategoryCheckpointCodec.StateKey.ToArray(), WikiCategoryCheckpointCodec.CompletionKey.ToArray() };
		foreach (var key in metaKeys)
		{
			Frame(hash, key);
			using var value = ((IBoundedReadTx)tx).ReadValue(Tables.Meta, key, budget) is NativeReadValue found ? found : null;
			hash.AppendData(new byte[] { value is null ? (byte)0 : (byte)1 });
			if (value is not null) Frame(hash, value.Value);
		}
		hash.AppendData(new byte[] { 0 });
		return Convert.ToHexString(hash.GetHashAndReset());
	});

	private static void RequireNeighborsAbsent(ITx tx)
	{
		Check(!tx.TryGet(Tables.Meta, NeighborAKey, out _) && !tx.TryGet(Tables.Meta, NeighborBKey, out _),
			"Failed group neighbor writes survived");
	}

	private sealed record ProducerProof(string role, bool faulted, int nativeCode, string operation);

	// One actual coordinator followed by two ordinary successful typed jobs, all queued before arm.
	private sealed class GroupCohort : IDisposable
	{
		private readonly LightningDatabase _db;
		private readonly WikiCategoryMigrationOptions _options;
		private readonly bool _resume;
		private readonly byte[] _source;
		private readonly byte[] _candidate = new byte[112];
		private readonly CancellationTokenSource _cancel = new();
		private readonly Task?[] _jobs = new Task?[3];
		private readonly Task[] _observations = [Task.CompletedTask, Task.CompletedTask, Task.CompletedTask];
		private readonly Exception?[] _errors = new Exception?[3];
		private int _jobCount;
		private int _neighborACallbacks;
		private int _neighborBCallbacks;
		internal bool AllQueuedPending { get; private set; }
		internal bool SameCommitException { get; private set; }
		internal int NeighborACallbacks => _neighborACallbacks;
		internal int NeighborBCallbacks => _neighborBCallbacks;
		internal bool NeighborASawCandidate { get; private set; }
		internal bool NeighborBSawCandidate { get; private set; }
		internal bool NeighborBSawA { get; private set; }

		internal GroupCohort(LightningDatabase db, bool resume, WikiCategoryMigrationOptions options)
		{
			_db = db;
			_resume = resume;
			_options = options;
			_source = db.Store.Read(tx =>
			{
				RequireNeighborsAbsent(tx);
				using var source = ((IBoundedReadTx)tx).ReadValue(Tables.WikiPage, PageKey,
					new(8, ProofCap, 4 * ProofCap, default)) is NativeReadValue found ? found : throw new InvalidDataException("Missing group source");
				return source.Value.ToArray();
			});
			// Independent literal first-batch oracle, not the checkpoint encoder under test.
			"WCM1"u8.CopyTo(_candidate);
			_candidate[5] = 1;
			_candidate[7] = 1;
			_candidate[8] = resume ? (byte)2 : (byte)1;
			_candidate[19] = resume ? (byte)2 : (byte)1;
			if (resume) _candidate[27] = 1;
		}

		internal async Task<LightningStoreException> FailAsync(KernelRestriction restriction)
		{
			var resumeAttempted = false;
			var settled = false;
			Exception? setupFailure = null;
			Exception? cleanupFailure = null;
			try
			{
				_db.Store.PauseWriter();
				Track(_db.MigrateWikiCategoryValuesAsync(_options, _cancel.Token).AsTask());
				Track(_db.Store.WriteAsync(tx =>
				{
					VerifyPendingCandidate(tx);
					RequireNeighborsAbsent(tx);
					NeighborASawCandidate = true;
					tx.Put(Tables.Meta, NeighborAKey, "pending-A"u8);
					Interlocked.Increment(ref _neighborACallbacks);
				}, _cancel.Token).AsTask());
				Track(_db.Store.WriteAsync(tx =>
				{
					VerifyPendingCandidate(tx);
					Check(tx.TryGet(Tables.Meta, NeighborAKey, out var a) && a.AsSpan().SequenceEqual("pending-A"u8)
						&& !tx.TryGet(Tables.Meta, NeighborBKey, out _), "Neighbor B did not see pending A");
					NeighborBSawCandidate = true;
					NeighborBSawA = true;
					tx.Put(Tables.Meta, NeighborBKey, "pending-B"u8);
					Interlocked.Increment(ref _neighborBCallbacks);
				}, _cancel.Token).AsTask());
				AllQueuedPending = _jobCount == 3 && _jobs.All(job => job is { IsCompleted: false });
				Check(AllQueuedPending && _neighborACallbacks == 0 && _neighborBCallbacks == 0, "Group did not queue while paused");
				var allObserved = Task.WhenAll(_observations);
				restriction.Arm();
				// Mark before Set: an unexpected Resume failure cannot fall through native disposal.
				resumeAttempted = true;
				try { _db.Store.ResumeWriter(); }
				catch { FatalUnsettled(); }
				await ObserveOrExit(allObserved, TimeSpan.FromSeconds(20));
				settled = true;
			}
			catch (Exception error) { setupFailure = error; }
			finally
			{
				if (resumeAttempted && !settled) FatalUnsettled();
				// No producer can run after this point unless setup failed before Resume; those jobs remain parked.
				restriction.RestoreOrExit();
				if (!settled)
				{
					// Failed admission/partial arm: restore first, cancel parked jobs, resume, observe every original.
					try { _cancel.Cancel(); }
					catch (Exception error) { cleanupFailure = error; }
					try { _db.Store.ResumeWriter(); }
					catch { FatalUnsettled(); }
					await ObserveOrExit(Task.WhenAll(_observations), TimeSpan.FromSeconds(10));
				}
			}
			if (setupFailure is not null && cleanupFailure is not null) throw new AggregateException(setupFailure, cleanupFailure);
			if (setupFailure is not null) ExceptionDispatchInfo.Capture(setupFailure).Throw();
			if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
			Check(_jobCount == 3 && _jobs.All(job => job is { IsFaulted: true }), "A group producer did not fail");
			Check(_neighborACallbacks == 1 && _neighborBCallbacks == 1 && NeighborASawCandidate && NeighborBSawCandidate && NeighborBSawA,
				"Ordinary group callbacks did not both return after pending-state verification");
			if (_errors[0] is not LightningStoreException failure) throw new InvalidDataException("Category producer did not report native failure");
			SameCommitException = _errors.All(error => ReferenceEquals(error, failure));
			var stack = failure.StackTrace ?? "";
			Check(SameCommitException && (int)failure.Code == 27 && failure.Message.StartsWith("commit:", StringComparison.Ordinal)
				&& stack.Contains("LightningStore.Commit", StringComparison.Ordinal)
				&& stack.Contains("LightningStore.WriteBatch", StringComparison.Ordinal)
				&& stack.Contains("LightningWriter.RunBatch", StringComparison.Ordinal), "Group did not share one actual parent COMMIT failure");
			return failure;
		}

		private void Track(Task job)
		{
			var index = _jobCount++;
			_jobs[index] = job;
			_observations[index] = Observe(index, job);
			Check(!job.IsCompleted, "Producer did not enter the paused queue");
		}

		private async Task Observe(int index, Task job)
		{
			try { await job; }
			catch (Exception error) { _errors[index] = error; }
		}

		private async Task ObserveOrExit(Task observed, TimeSpan deadline)
		{
			try
			{
				await observed.WaitAsync(deadline);
				for (var index = 0; index < _jobCount; index++)
					if (_jobs[index] is not { IsCompleted: true }) FatalUnsettled();
			}
			catch { FatalUnsettled(); }
		}

		private void VerifyPendingCandidate(ITx tx)
		{
			Check(tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, out var state)
				&& state.AsSpan().SequenceEqual(_candidate) && !tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, out _),
				"Neighbor did not see the exact pending coordinator checkpoint");
			Check(tx.Count(Tables.WikiPage) == 1 && tx.TryGet(Tables.WikiPage, PageKey, out var source)
				&& source.AsSpan().SequenceEqual(_source), "Pending group source changed");
			Check(tx.Count(Tables.WikiByCategory) == (_resume ? 0 : 1), "Pending category index differs");
			if (!_resume) Check(tx.TryGet(Tables.WikiByCategory, new byte[] { 1, 2 }, out var old)
				&& old.AsSpan().SequenceEqual(new byte[] { 1 }), "Pending obsolete category row differs");
		}

		internal ProducerProof[] Producers()
			=> [Producer(0, "category"), Producer(1, "neighbor-a"), Producer(2, "neighbor-b")];

		private ProducerProof Producer(int index, string role)
		{
			if (_errors[index] is not LightningStoreException error) throw new InvalidDataException("Missing producer native exception");
			return new(role, _jobs[index] is { IsFaulted: true }, (int)error.Code,
				error.Message.StartsWith("commit:", StringComparison.Ordinal) ? "commit" : "invalid");
		}

		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		private static void FatalUnsettled()
		{
			try { Console.Error.WriteLine("category group producer settlement unconfirmed"); Console.Error.Flush(); }
			finally { Environment.Exit(125); }
			throw new InvalidOperationException("Unreachable after Environment.Exit");
		}

		public void Dispose() => _cancel.Dispose();
	}

	private static void Frame(IncrementalHash hash, ReadOnlySpan<byte> bytes)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
		hash.AppendData(length);
		hash.AppendData(bytes);
	}

	private static string SourceHash(LightningDatabase db) => db.Store.Read(tx =>
	{
		using var source = ((IBoundedReadTx)tx).ReadValue(Tables.WikiPage, PageKey,
			new(8, ProofCap, 4 * ProofCap, default)) is NativeReadValue found ? found : throw new InvalidDataException("Missing source");
		return Convert.ToHexString(SHA256.HashData(source.Value));
	});

	private static (string Path, string Hash) LoadedProvenance()
	{
		// Inspect the existing process's loaded module; never NativeLibrary.Load a second copy.
		using var process = Process.GetCurrentProcess();
		var modules = process.Modules.Cast<ProcessModule>().Where(module => Path.GetFileName(module.FileName) == "liblmdb.so").ToArray();
		Check(modules.Length == 1, "Expected one already-loaded liblmdb module");
		var loaded = Path.GetFullPath(modules[0].FileName);
		Check(loaded.Length <= 1024, "Native module path too long");
		var deps = Path.Join(AppContext.BaseDirectory, "WikiSemantics.NativeProbe.deps.json");
		using var manifest = JsonDocument.Parse(ReadBounded(deps, 4 << 20));
		var library = manifest.RootElement.GetProperty("libraries").GetProperty("LightningDB/0.23.1");
		Check(library.GetProperty("path").GetString() == "lightningdb/0.23.1", "Wrong resolved LightningDB package");
		var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
			?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
		var package = Path.Join(packages, "lightningdb", "0.23.1");
		var packagedNative = Path.Join(package, "runtimes", "linux-x64", "native", "liblmdb.so");
		var hash = Convert.ToHexString(SHA256.HashData(ReadBounded(loaded, 16 << 20)));
		Check(hash == Convert.ToHexString(SHA256.HashData(ReadBounded(packagedNative, 16 << 20))), "Loaded native module differs from resolved package");
		Check(Encoding.UTF8.GetString(ReadBounded(Path.Join(package, "wasm", "README.md"), 65536)).Contains("LMDB_1.0.1", StringComparison.Ordinal)
			&& Encoding.UTF8.GetString(ReadBounded(Path.Join(package, "wasm", "mdb.c"), 2 << 20)).Contains("#define NUM_METAS\t2", StringComparison.Ordinal),
			"Missing pinned LMDB metadata-page provenance");
		return (loaded, hash);
	}

	private static byte[] ReadBounded(string path, int cap)
	{
		using var stream = File.OpenRead(path);
		Check(stream.Length <= cap, "Provenance file exceeds cap");
		var bytes = new byte[checked(cap + 1)];
		var total = 0;
		while (total < bytes.Length)
		{
			var count = stream.Read(bytes, total, bytes.Length - total);
			if (count == 0) break;
			total += count;
		}
		Check(total <= cap, "Provenance file grew beyond cap");
		return bytes.AsSpan(0, total).ToArray();
	}

	private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
	private static void RequireAbsent(string path)
	{
		RejectLinkedAncestors(path);
		Check(!File.Exists(path) && !Directory.Exists(path), "Owned path already exists");
	}
	private static void RejectLinkedAncestors(string path)
	{
		for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
		{
			Check(new FileInfo(current).LinkTarget is null && new DirectoryInfo(current).LinkTarget is null, "Linked owned path");
			if (File.Exists(current) || Directory.Exists(current)) Check((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Reparse owned path");
		}
	}

	private sealed class UnusedPasswordService : IPasswordService
	{
		public string HashPassword(string pw) => throw new NotSupportedException();
		public bool PasswordIsValid(string pw, string hash) => throw new NotSupportedException();
		public ValueTask SetPassword(SharpPlayer user, string hashedPassword) => throw new NotSupportedException();
		public string GenerateRandomPassword() => throw new NotSupportedException();
		public bool NeedsRehash(string hash) => throw new NotSupportedException();
		public ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext) => throw new NotSupportedException();
	}

	private sealed class KernelRestriction : IDisposable
	{
		[StructLayout(LayoutKind.Sequential)]
		internal struct Limit { internal ulong Soft; internal ulong Hard; }
		[StructLayout(LayoutKind.Sequential)]
		private struct KernelAction { internal ulong Handler; internal ulong Flags; internal ulong Restorer; internal ulong Mask; }
		private readonly KernelAction _prior;
		private readonly ulong _soft;
		private bool _signalTouched;
		private bool _limitTouched;
		internal Limit Before { get; }
		internal Limit After { get; private set; }
		internal string BeforeHash { get; }
		internal string AfterHash { get; private set; } = "";

		internal static void RequireAbi()
		{
			if (!OperatingSystem.IsLinux() || RuntimeInformation.OSArchitecture != Architecture.X64
				|| RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8 || !BitConverter.IsLittleEndian)
				throw new PlatformNotSupportedException("Native category proof unavailable: requires Linux OS/process x64 little-endian pointer8");
			Check(Marshal.SizeOf<Limit>() == 16 && Marshal.SizeOf<KernelAction>() == 32, "Unsupported kernel ABI layout");
		}
		internal KernelRestriction(ulong soft)
		{
			RequireAbi();
			Check(GetLimit(1, out var before) == 0, "getrlimit failed");
			Before = before;
			Check(soft <= before.Hard && soft < before.Soft, "Soft limit cannot be lowered to metadata boundary");
			Check(QueryAction(13, 25, IntPtr.Zero, out _prior, 8) == 0, "rt_sigaction query failed");
			BeforeHash = ActionHash(_prior);
			_soft = soft;
			// Warm the exact setter bridge with unchanged state, then verify all fields unchanged.
			var prior = _prior;
			var limit = before;
			try
			{
				_signalTouched = true;
				Check(SetAction(13, 25, ref prior, IntPtr.Zero, 8) == 0, "Signal interop warmup failed");
				_limitTouched = true;
				Check(SetLimit(1, ref limit) == 0, "Limit interop warmup failed");
			}
			finally { RestoreOrExit(); }
		}
		internal void Arm()
		{
			var ignored = new KernelAction { Handler = 1, Flags = 0, Restorer = 0, Mask = 0 };
			_signalTouched = true;
			Check(SetAction(13, 25, ref ignored, IntPtr.Zero, 8) == 0, "rt_sigaction install failed");
			var limit = new Limit { Soft = _soft, Hard = Before.Hard };
			_limitTouched = true;
			Check(SetLimit(1, ref limit) == 0, "setrlimit arm failed");
			Check(GetLimit(1, out var actual) == 0 && actual.Soft == _soft && actual.Hard == Before.Hard, "Armed limit mismatch");
			Check(QueryAction(13, 25, IntPtr.Zero, out var action, 8) == 0 && Equal(action, ignored), "Armed signal mismatch");
		}
		internal void RestoreOrExit()
		{
			try
			{
				var limit = Before;
				var prior = _prior;
				var limitResult = !_limitTouched || SetLimit(1, ref limit) == 0;
				// Always attempt both restorations, even when the first one failed.
				var signalResult = !_signalTouched || SetAction(13, 25, ref prior, IntPtr.Zero, 8) == 0;
				var limitQuery = GetLimit(1, out var after);
				var signalQuery = QueryAction(13, 25, IntPtr.Zero, out var action, 8);
				if (!limitResult || !signalResult || limitQuery != 0
					|| after.Soft != Before.Soft || after.Hard != Before.Hard
					|| signalQuery != 0 || !Equal(action, _prior)) Fatal();
				After = after;
				AfterHash = ActionHash(action);
				_signalTouched = false;
				_limitTouched = false;
			}
			catch { Fatal(); }
		}
		private static bool Equal(KernelAction a, KernelAction b)
			=> a.Handler == b.Handler && a.Flags == b.Flags && a.Restorer == b.Restorer && a.Mask == b.Mask;
		private static string ActionHash(KernelAction action)
		{
			Span<byte> bytes = stackalloc byte[32];
			BinaryPrimitives.WriteUInt64BigEndian(bytes, action.Handler);
			BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], action.Flags);
			BinaryPrimitives.WriteUInt64BigEndian(bytes[16..], action.Restorer);
			BinaryPrimitives.WriteUInt64BigEndian(bytes[24..], action.Mask);
			return Convert.ToHexString(SHA256.HashData(bytes));
		}
		[System.Diagnostics.CodeAnalysis.DoesNotReturn]
		private static void Fatal()
		{
			try { Console.Error.WriteLine("category native restriction restoration failed"); Console.Error.Flush(); }
			finally { Environment.Exit(126); }
			throw new InvalidOperationException("Unreachable after Environment.Exit");
		}
		public void Dispose() { if (_signalTouched || _limitTouched) RestoreOrExit(); }
		// Linux x86_64 kernel rt_sigaction syscall13: handler, flags, restorer, mask; sigsetsize8.
		// These are fixed-signature declarations for libc's variadic syscall bridge, with 64-bit arguments.
		[DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
		private static extern long QueryAction(long number, long signal, IntPtr action, out KernelAction oldAction, ulong sigsetsize);
		[DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
		private static extern long SetAction(long number, long signal, ref KernelAction action, IntPtr oldAction, ulong sigsetsize);
		[DllImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
		private static extern int GetLimit(int resource, out Limit limit);
		[DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
		private static extern int SetLimit(int resource, ref Limit limit);
	}
}
