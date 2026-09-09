using System.Collections.Immutable;
using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Owns the LMDB environment and the open table handles. Reads run on the calling thread inside
/// <see cref="Read{T}"/>; writes are serialized through <see cref="LightningWriter"/>. The
/// environment can be closed and reopened under <see cref="Gate"/> so staging promotion can swap the
/// directory beneath a live singleton.
/// </summary>
public sealed partial class LightningStore : IDisposable
{
	private readonly LightningStoreOptions _options;
	private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);
	private readonly LightningWriter _writer;
	private LightningEnvironment _env = null!;

	/// <summary>Volatile: <see cref="Open"/>, <see cref="Close"/> and <see cref="OpenTable"/> swap in a whole
	/// new dictionary, and reader threads outside the gate's write lock must see the swap, never a stale one.</summary>
	private volatile Dictionary<TableDef, LmdbDb> _tables = new();

	private bool _disposed;

	/// <summary>Forces a sync every <see cref="LightningStoreOptions.FlushInterval"/> under
	/// <see cref="LightningSyncMode.Periodic"/>; null in the other modes, where every commit syncs itself.</summary>
	private readonly Timer? _flushTimer;
	private long _commits;
	private long _flushes;
	/// <summary>Non-zero while a commit has landed since the last forced flush. The timer skips its sync
	/// when nothing is dirty, so an idle world costs no disk traffic.</summary>
	private int _unflushed;
	/// <summary>Set when a periodic flush fails. A timer callback has no caller to throw to, and an
	/// unhandled exception there is process death; instead every write from then on fails with this, so
	/// the failure surfaces where someone is listening and no further commit lands on a disk that could
	/// not take the last one.</summary>
	private volatile Exception? _flushFailure;

	/// <summary>Top-level write transactions committed so far. Observable batching: a burst of N jobs that
	/// queued behind one in-flight commit shows up here as one commit, not N.</summary>
	internal long CommitCount => Interlocked.Read(ref _commits);
	/// <summary>Forced syncs the periodic timer has run.</summary>
	internal long FlushCount => Interlocked.Read(ref _flushes);

	/// <summary>Tables opened through <see cref="OpenTable"/> rather than declared in <see cref="Tables"/>
	/// — a plugin's own. Kept across <see cref="Close"/> so <see cref="Open"/> reopens them too: after a
	/// directory swap a plugin still holds the <see cref="TableDef"/> it was handed, and that definition
	/// has to keep resolving to a live handle.</summary>
	private ImmutableDictionary<string, TableDef> _pluginTables = ImmutableDictionary<string, TableDef>.Empty;

	public string Path => _options.Path;
	internal ReaderWriterLockSlim Gate => _gate;

	public LightningStore(LightningStoreOptions options)
	{
		// Reject bad options before anything is acquired: a throw after Open would leave the environment
		// mapped and its lock file held, with no store handed back to dispose them.
		if (options.MaxBatch < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(options), options.MaxBatch, "MaxBatch holds at least one job.");
		}

		if (options.Sync == LightningSyncMode.Periodic && options.FlushInterval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(options), options.FlushInterval, "FlushInterval must be positive under Periodic sync.");
		}

		_options = options;
		Open();
		_writer = new LightningWriter(WriteBatch, rawWork => WriteRawInternal(rawWork), options.MaxBatch);
		if (options.Sync == LightningSyncMode.Periodic)
		{
			_flushTimer = new Timer(_ => FlushIfDirty(), null, options.FlushInterval, options.FlushInterval);
		}
	}

	/// <summary>The timer's tick. Skips when nothing has been committed since the last flush, and skips
	/// rather than blocks when the environment is mid-swap: <see cref="Close"/> flushes on its own way
	/// out, so a tick that cannot get the read lock has nothing left to do.</summary>
	private void FlushIfDirty()
	{
		if (_disposed || Interlocked.Exchange(ref _unflushed, 0) == 0) return;
		if (!_gate.TryEnterReadLock(0)) { Interlocked.Exchange(ref _unflushed, 1); return; }
		try
		{
			var code = _env.Flush(force: true);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "flush");
			Interlocked.Increment(ref _flushes);
		}
		catch (Exception ex)
		{
			_flushFailure = ex;
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	private static EnvironmentOpenFlags FlagsFor(LightningSyncMode sync) => sync switch
	{
		LightningSyncMode.Full => EnvironmentOpenFlags.NoThreadLocalStorage,
		LightningSyncMode.NoMetaSync => EnvironmentOpenFlags.NoThreadLocalStorage | EnvironmentOpenFlags.NoMetaSync,
		// NoSync, never WriteMap|MapAsync: that pair is the one LMDB documents as able to corrupt the file.
		LightningSyncMode.Periodic => EnvironmentOpenFlags.NoThreadLocalStorage | EnvironmentOpenFlags.NoSync,
		_ => throw new ArgumentOutOfRangeException(nameof(sync), sync, "Unknown sync mode.")
	};

	private void Open()
	{
		Directory.CreateDirectory(_options.Path);
		_env = new LightningEnvironment(_options.Path, new EnvironmentConfiguration
		{
			MapSize = _options.MapSize,
			MaxDatabases = _options.MaxDatabases,
			MaxReaders = _options.MaxReaders,
			PageSize = _options.PageSize
		});
		_env.Open(FlagsFor(_options.Sync));

		using var tx = _env.BeginTransaction();
		var tables = new Dictionary<TableDef, LmdbDb>();
		foreach (var def in Tables.All.Concat(_pluginTables.Values))
		{
			tables[def] = tx.OpenDatabase(def.Name, new DatabaseConfiguration { Flags = FlagsFor(def) | DatabaseOpenFlags.Create });
		}
		var openCode = tx.Commit();
		if (openCode != MDBResultCode.Success) throw LightningStoreException.From(openCode, "open");
		_tables = tables;
		// A fresh environment: whatever a periodic flush failed to do belonged to the old one, and the
		// close that ended it has already reported (or is about to report) its own result.
		_flushFailure = null;
	}

	internal static DatabaseOpenFlags FlagsFor(TableDef def) => def.Duplicates
		? DatabaseOpenFlags.DuplicatesSort | (def.FixedDuplicates ? DatabaseOpenFlags.DuplicatesFixed : DatabaseOpenFlags.None)
		: DatabaseOpenFlags.None;

	/// <summary>Closes the environment and returns the result of the final forced flush the relaxed sync
	/// modes need on the way out (<see cref="MDBResultCode.Success"/> under Full, which has nothing
	/// unflushed). The environment is disposed whichever way that flush went; the caller decides how to
	/// report a failure once its own cleanup is complete.</summary>
	internal MDBResultCode Close()
	{
		foreach (var db in _tables.Values) db.Dispose();
		_tables = new();
		var flushed = MDBResultCode.Success;
		if (_options.Sync != LightningSyncMode.Full)
		{
			// Whatever the relaxed mode left in the page cache goes to disk before the directory is
			// closed, moved or deleted.
			flushed = _env.Flush(force: true);
			Interlocked.Exchange(ref _unflushed, 0);
		}
		_env.Dispose();
		return flushed;
	}

	/// <summary>
	/// Opens (creating if needed) a table this environment's own <see cref="Tables"/> catalogue does not
	/// know about — a plugin's own tables, opened on the plugin's first use rather than baked into the
	/// core schema. Idempotent by name: a second call for the same name returns the existing definition
	/// rather than reopening the handle. The open+commit itself runs as a raw job on the writer thread
	/// (see <see cref="WriteRaw{T}"/>) because opening a sub-database needs the raw
	/// <see cref="LightningTransaction"/>, which <see cref="ITx"/> does not expose; the writer thread's own
	/// single-job-at-a-time processing is what makes the "check, then open" idempotency check race-free,
	/// not any lock on this method. Must not be called from inside a write job — see <see cref="WriteRaw{T}"/>.
	/// </summary>
	internal TableDef OpenTable(string name, bool duplicates)
	{
		var existing = FindTable(name);
		if (existing is not null)
		{
			return existing;
		}

		return WriteRaw(tx =>
		{
			// Re-check inside the job: two concurrent callers can both queue an OpenTable job for the
			// same new name before either one runs; only the first should actually open the database.
			var alreadyOpened = FindTable(name);
			if (alreadyOpened is not null)
			{
				return alreadyOpened;
			}

			var def = duplicates ? TableDef.Index(name, duplicates: true) : TableDef.Node(name);
			var handle = tx.OpenDatabase(def.Name, new DatabaseConfiguration { Flags = FlagsFor(def) | DatabaseOpenFlags.Create });
			var code = tx.Commit();
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"open table {name}");
			_tables = new Dictionary<TableDef, LmdbDb>(_tables) { [def] = handle };
			_pluginTables = _pluginTables.SetItem(name, def);
			return def;
		});
	}

	private TableDef? FindTable(string name) => _tables.Keys.FirstOrDefault(t => t.Name == name);

	public T Read<T>(Func<ITx, T> read)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction(TransactionBeginFlags.ReadOnly);
			return read(new Tx(tx, _tables));
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	/// <summary>
	/// Runs one group commit on the calling thread; only <see cref="LightningWriter"/>'s thread may call
	/// this. A single item runs directly in the top-level transaction, exactly as a lone write always has.
	/// Several items each get a nested transaction under one parent: an item that throws (its own code,
	/// or a failed put) has its child aborted and its <see cref="LightningWriter.BatchItem.Error"/> set,
	/// and the parent carries on with the rest. The parent then commits once — one sync for the whole
	/// batch. If that commit fails, this throws and the writer faults every item that had not already
	/// failed on its own; nothing from the batch reached the disk.
	/// </summary>
	private void WriteBatch(IReadOnlyList<LightningWriter.BatchItem> items)
	{
		ThrowIfFlushFailed();
		_gate.EnterReadLock();
		try
		{
			using var parent = _env.BeginTransaction();
			if (items.Count == 1)
			{
				var only = items[0];
				try
				{
					only.Result = only.Work(new Tx(parent, _tables));
				}
				catch (Exception ex)
				{
					// Disposing the uncommitted parent aborts it.
					only.Error = ex;
					return;
				}

				Commit(parent);
				return;
			}

			foreach (var item in items)
			{
				using var child = _env.BeginTransaction(parent);
				try
				{
					item.Result = item.Work(new Tx(child, _tables));
					var code = child.Commit();
					if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "commit");
				}
				catch (Exception ex)
				{
					// Disposing the uncommitted child aborts it; the parent is untouched by this item.
					item.Error = ex;
				}
			}

			Commit(parent);
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	private void ThrowIfFlushFailed()
	{
		if (_flushFailure is { } failure)
		{
			throw new InvalidOperationException("A periodic flush failed; the store refuses further writes until it is reopened.", failure);
		}
	}

	private void Commit(LightningTransaction tx)
	{
		var code = tx.Commit();
		if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "commit");
		Interlocked.Increment(ref _commits);
		Interlocked.Exchange(ref _unflushed, 1);
	}

	/// <summary>Direct raw write on the calling thread, same read-gate discipline as <see cref="Write{T}"/> but
	/// without <see cref="ITx"/>'s wrapping or auto-commit — <paramref name="job"/> owns the transaction and must
	/// commit it itself. Only <see cref="LightningWriter"/>'s thread may call this.</summary>
	private T WriteRawInternal<T>(Func<LightningTransaction, T> job)
	{
		ThrowIfFlushFailed();
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction();
			var result = job(tx);
			// The job owns its commit, so the store cannot count it — but under Periodic the pages it wrote
			// are unflushed like any other, and the timer must not skip them.
			Interlocked.Exchange(ref _unflushed, 1);
			return result;
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _writer.EnqueueAsync(job, ct);

	public async ValueTask WriteAsync(Action<ITx> job, CancellationToken ct = default)
		=> await _writer.EnqueueAsync<object?>(tx => { job(tx); return null; }, ct).ConfigureAwait(false);

	/// <summary>
	/// Queues <paramref name="job"/> onto the writer thread like any other write, but hands it the raw
	/// <see cref="LightningTransaction"/> instead of an <see cref="ITx"/> — for callers that need LMDB APIs
	/// <see cref="ITx"/> does not expose (e.g. <see cref="OpenTable"/> opening a sub-database) and must
	/// commit the transaction themselves. Blocks the calling thread until the writer thread runs the job.
	/// Must not be called from inside a write job (a job already running on the writer thread) — it would
	/// wait forever on the writer thread that is running it.
	/// </summary>
	internal T WriteRaw<T>(Func<LightningTransaction, T> job)
		=> _writer.EnqueueRawAsync(job, CancellationToken.None).AsTask().GetAwaiter().GetResult();

	internal void PauseWriter() => _writer.Pause();
	internal void ResumeWriter() => _writer.Resume();

	/// <summary>
	/// Replaces this environment's directory with <paramref name="incomingPath"/>, keeping the outgoing
	/// one at <paramref name="previousPath"/> (an older copy there is deleted first). The sequence is:
	/// drain and park the writer, take the gate's write lock, close the environment, move live aside,
	/// move the incoming directory into the live path, reopen (every catalogue table and every
	/// plugin-opened one), release the lock, resume the writer. Reads that arrive meanwhile block on the
	/// gate and then run against the swapped-in environment; they never see a closed one and never fail.
	/// Blocks the calling thread, which must not be the writer thread.
	/// </summary>
	internal void SwapDirectory(string incomingPath, string previousPath)
	{
		if (!Directory.Exists(incomingPath))
		{
			throw new DirectoryNotFoundException($"Cannot swap in '{incomingPath}': the directory does not exist.");
		}

		WhileClosed(() =>
		{
			if (Directory.Exists(previousPath)) Directory.Delete(previousPath, recursive: true);
			Directory.Move(_options.Path, previousPath);
			try
			{
				Directory.Move(incomingPath, _options.Path);
			}
			catch
			{
				// The live directory is already aside and the environment is closed: put it back so the
				// caller is left with a working store rather than no directory at all.
				Directory.Move(previousPath, _options.Path);
				throw;
			}
		});
	}

	/// <summary>Closes the environment, deletes the directory and reopens it empty. Same writer and gate
	/// discipline as <see cref="SwapDirectory"/>; the caller re-migrates.</summary>
	internal void WipeDirectory() => WhileClosed(() =>
	{
		if (Directory.Exists(_options.Path)) Directory.Delete(_options.Path, recursive: true);
	});

	/// <summary>Runs <paramref name="onDisk"/> with no writer running, no reader inside a transaction and
	/// the environment closed, then reopens it. The writer is drained and parked first so no committed
	/// write is left behind in the directory being moved away. <see cref="Open"/> runs whether or not
	/// <paramref name="onDisk"/> succeeded: a throw there costs the caller its operation, not the store —
	/// leaving the environment closed while the gate is released and the writer resumed would fail every
	/// read and write that follows.</summary>
	private void WhileClosed(Action onDisk)
	{
		_writer.PauseAndDrainAsync().GetAwaiter().GetResult();
		try
		{
			_gate.EnterWriteLock();
			try
			{
				var closed = Close();
				try
				{
					onDisk();
				}
				finally
				{
					Open();
				}

				// Reported only now, with a live environment behind the gate again: the pages that flush
				// failed to write were in the directory just moved or deleted, and the caller has to know.
				if (closed != MDBResultCode.Success) throw LightningStoreException.From(closed, "flush on close");
			}
			finally
			{
				_gate.ExitWriteLock();
			}
		}
		finally
		{
			ResumeWriter();
		}
	}

	public long Count(TableDef table) => Read(tx => tx.Count(table));

	/// <summary>Hot backup of the whole environment into <paramref name="path"/>. A read-side operation:
	/// it takes the gate's read lock (so it cannot race a swap) and never touches the writer, so writes
	/// continue while it runs and the copy is the snapshot of the transaction it opens.</summary>
	public void CopyTo(string path, bool compact = true)
	{
		Directory.CreateDirectory(path);
		_gate.EnterReadLock();
		try
		{
			var code = _env.CopyTo(path, compact);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "copy");
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	/// <summary>Idempotent: the store is owned by <c>LightningDatabase</c>, which the host's container also
	/// disposes, so a second call has to be a no-op rather than an <see cref="ObjectDisposedException"/>.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (_flushTimer is not null)
		{
			// Dispose(WaitHandle) waits for a tick already inside FlushIfDirty, so the environment is never
			// closed underneath a flush in flight.
			using var ticked = new ManualResetEvent(false);
			if (_flushTimer.Dispose(ticked)) ticked.WaitOne();
		}
		_writer.Dispose();
		var closed = Close();
		_gate.Dispose();
		// Every handle is released first; a failed final flush is then the one thing left to say, and
		// silence here would let committed writes go non-durable with nobody told.
		if (closed != MDBResultCode.Success) throw LightningStoreException.From(closed, "flush on close");
	}

	private sealed class Tx(LightningTransaction tx, Dictionary<TableDef, LmdbDb> tables) : ITx
	{
		private LmdbDb Db(TableDef t) => tables[t];

		public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value)
		{
			var (code, _, v) = tx.Get(Db(table), key);
			if (code == MDBResultCode.NotFound) { value = []; return false; }
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"get {table}");
			value = v.CopyToNewArray();
			return true;
		}

		public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		{
			var code = tx.Put(Db(table), key, value);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"put {table}");
		}

		public bool Delete(TableDef table, ReadOnlySpan<byte> key)
		{
			var code = tx.Delete(Db(table), key);
			if (code == MDBResultCode.NotFound) return false;
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"delete {table}");
			return true;
		}

		public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		{
			var code = tx.Delete(Db(table), key, value);
			if (code == MDBResultCode.NotFound) return false;
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"delete {table}");
			return true;
		}

		public long Count(TableDef table) => tx.GetEntriesCount(Db(table));

		public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix)
		{
			using var cursor = tx.CreateCursor(Db(table));
			var positioned = prefix.Length == 0 ? cursor.First().resultCode : cursor.SetRange(prefix);
			if (positioned != MDBResultCode.Success) yield break;
			do
			{
				var (code, k, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				// The prefix test reads the mapped page directly; only an entry that is yielded is copied out.
				if (!Keys.StartsWith(k.AsSpan(), prefix)) yield break;
				yield return (k.CopyToNewArray(), v.CopyToNewArray());
			} while (cursor.Next().resultCode == MDBResultCode.Success);
		}

		/// <summary>
		/// <see cref="Range"/> resumed after the entry (<paramref name="afterKey"/>, <paramref name="afterValue"/>):
		/// the cursor seeks straight to <paramref name="afterKey"/> rather than rescanning the prefix from its
		/// start, so a paged scan costs one seek per page instead of one re-read of everything already yielded.
		/// Landing exactly on <paramref name="afterKey"/> means part of that key was yielded already — its
		/// duplicate run is walked forward until past <paramref name="afterValue"/>, or the whole key is stepped
		/// over when <paramref name="afterValue"/> is <see langword="null"/> (a table with no duplicates, or a
		/// caller resuming on the key alone).
		/// </summary>
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue)
		{
			using var cursor = tx.CreateCursor(Db(table));
			// A resume point before the prefix cannot be seeked to without dropping the rows between the two;
			// start at the prefix instead, where nothing has been yielded yet and nothing needs skipping.
			var resumeIsInsidePrefix = afterKey.AsSpan().SequenceCompareTo(prefix) >= 0;
			var positioned = resumeIsInsidePrefix ? cursor.SetRange(afterKey) : cursor.SetRange(prefix);
			if (positioned != MDBResultCode.Success) yield break;

			while (resumeIsInsidePrefix)
			{
				var (code, k, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				if (!k.AsSpan().SequenceEqual(afterKey)) break;
				if (afterValue is not null && v.AsSpan().SequenceCompareTo(afterValue) > 0) break;
				if (cursor.Next().resultCode != MDBResultCode.Success) yield break;
			}

			do
			{
				var (code, k, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				if (!Keys.StartsWith(k.AsSpan(), prefix)) yield break;
				yield return (k.CopyToNewArray(), v.CopyToNewArray());
			} while (cursor.Next().resultCode == MDBResultCode.Success);
		}

		public IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey)
		{
			using var cursor = tx.CreateCursor(Db(table));
			var positioned = cursor.SetRange(startKey);
			if (positioned != MDBResultCode.Success) yield break;
			do
			{
				var (code, k, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				yield return (k.CopyToNewArray(), v.CopyToNewArray());
			} while (cursor.Next().resultCode == MDBResultCode.Success);
		}

		public IEnumerable<byte[]> Dups(TableDef table, byte[] key)
		{
			using var cursor = tx.CreateCursor(Db(table));
			if (cursor.Set(key) != MDBResultCode.Success) yield break;
			do
			{
				var (code, _, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				yield return v.CopyToNewArray();
			} while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
		}

		public int DeletePrefix(TableDef table, byte[] prefix)
		{
			// Materialized: the cursor behind Range must not be walked while its rows are being deleted.
			var keys = Range(table, prefix).ToList();
			foreach (var (k, v) in keys)
			{
				if (table.Duplicates) tx.Delete(Db(table), k, v);
				else tx.Delete(Db(table), k);
			}
			return keys.Count;
		}
	}
}
