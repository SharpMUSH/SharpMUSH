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
	private Dictionary<TableDef, LmdbDb> _tables = new();

	public string Path => _options.Path;
	internal ReaderWriterLockSlim Gate => _gate;

	public LightningStore(LightningStoreOptions options)
	{
		_options = options;
		Open();
		_writer = new LightningWriter(work => Write(work), rawWork => WriteRawInternal(rawWork));
	}

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
		_env.Open(EnvironmentOpenFlags.NoThreadLocalStorage);

		using var tx = _env.BeginTransaction();
		var tables = new Dictionary<TableDef, LmdbDb>();
		foreach (var def in Tables.All)
		{
			tables[def] = tx.OpenDatabase(def.Name, new DatabaseConfiguration { Flags = FlagsFor(def) | DatabaseOpenFlags.Create });
		}
		var openCode = tx.Commit();
		if (openCode != MDBResultCode.Success) throw LightningStoreException.From(openCode, "open");
		_tables = tables;
	}

	internal static DatabaseOpenFlags FlagsFor(TableDef def) => def.Duplicates
		? DatabaseOpenFlags.DuplicatesSort | (def.FixedDuplicates ? DatabaseOpenFlags.DuplicatesFixed : DatabaseOpenFlags.None)
		: DatabaseOpenFlags.None;

	internal void Close()
	{
		foreach (var db in _tables.Values) db.Dispose();
		_tables = new();
		_env.Dispose();
	}

	internal void Reopen() => Open();

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

	/// <summary>Direct write on the calling thread. Only <see cref="LightningWriter"/>'s thread may call this.</summary>
	private T Write<T>(Func<ITx, T> write)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction();
			var result = write(new Tx(tx, _tables));
			var code = tx.Commit();
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "commit");
			return result;
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	/// <summary>Direct raw write on the calling thread, same read-gate discipline as <see cref="Write{T}"/> but
	/// without <see cref="ITx"/>'s wrapping or auto-commit — <paramref name="job"/> owns the transaction and must
	/// commit it itself. Only <see cref="LightningWriter"/>'s thread may call this.</summary>
	private T WriteRawInternal<T>(Func<LightningTransaction, T> job)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction();
			return job(tx);
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

	internal Task DrainAsync() => _writer.DrainAsync();
	internal void PauseWriter() => _writer.Pause();
	internal void ResumeWriter() => _writer.Resume();

	public long Count(TableDef table) => Read(tx => tx.Count(table));

	public void CopyTo(string path, bool compact = true)
	{
		Directory.CreateDirectory(path);
		var code = _env.CopyTo(path, compact);
		if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "copy");
	}

	public void Dispose()
	{
		_writer.Dispose();
		Close();
		_gate.Dispose();
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
				var key = k.CopyToNewArray();
				if (!Keys.StartsWith(key, prefix)) yield break;
				yield return (key, v.CopyToNewArray());
			} while (cursor.Next().resultCode == MDBResultCode.Success);
		}

		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue)
		{
			foreach (var entry in Range(table, prefix))
			{
				var keyCompare = entry.Key.AsSpan().SequenceCompareTo(afterKey);
				if (keyCompare < 0) continue;
				if (keyCompare == 0 && (afterValue is null || entry.Value.AsSpan().SequenceCompareTo(afterValue) <= 0)) continue;
				yield return entry;
			}
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
			var keys = Range(table, prefix).Select(e => (e.Key, e.Value)).ToList();
			foreach (var (k, v) in keys)
			{
				if (table.Duplicates) tx.Delete(Db(table), k, v);
				else tx.Delete(Db(table), k);
			}
			return keys.Count;
		}
	}
}
