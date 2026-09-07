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
		_writer = new LightningWriter(work => Write(work));
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

	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _writer.EnqueueAsync(job, ct);

	public async ValueTask WriteAsync(Action<ITx> job, CancellationToken ct = default)
		=> await _writer.EnqueueAsync<object?>(tx => { job(tx); return null; }, ct).ConfigureAwait(false);

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
