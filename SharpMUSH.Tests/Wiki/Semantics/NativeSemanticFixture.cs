using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

internal sealed class NativeSemanticFixture : ILightningStorageAccessor, IDisposable
{
	private LightningStore _store;
	private readonly long _mapSize;
	public string Path { get; }
	public static SemanticSchema Schema => new(1, [new("test:i", 1, SemanticType.Integer, Cardinality.Multiple, []),
		new("test:p", 1, SemanticType.PageRef, Cardinality.Multiple, []), new("test:k", 1, SemanticType.Keyword, Cardinality.Multiple, []), new("test:b", 1, SemanticType.Boolean, Cardinality.Single, [])]);
	public NativeSemanticFixture(long mapSize = 256L << 20)
	{
		_mapSize = mapSize;
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? System.IO.Path.Combine(Environment.CurrentDirectory, "work/semantic-wiki-prototype/b1/tmp");
		Path = System.IO.Path.Combine(scratch, "semantic-" + Guid.NewGuid().ToString("N"));
		_store = Open();
	}
	private LightningStore Open() => new(new LightningStoreOptions { Path = Path, MapSize = _mapSize, Sync = LightningSyncMode.Full });
	public void Reopen() { _store.Dispose(); _store = Open(); }
	public T Read<T>(Func<ITx, T> read) => _store.Read(read);
	public int LastProbes { get; private set; }
	public long LastBytes { get; private set; }
	public long LastDeltaBytes { get; private set; }
	public long LastWorkingBytes { get; private set; }
	public double LastJobMilliseconds { get; private set; }
	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _store.WriteAsync(tx =>
	{
		var observed = new ObservedTx(tx);
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		try { return job(observed); }
		finally
		{
			LastProbes = observed.Probes; LastBytes = observed.Bytes;
			LastDeltaBytes = observed.DeltaBytes; LastWorkingBytes = observed.WorkingBytes;
			LastJobMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		}
	}, ct);
	public TableDef OpenTable(string name, bool duplicates) => _store.OpenTable(name, duplicates);
	public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default)
		=> _store.RangeAsync(table, prefix, pageSize, ct);
	public ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested(); _store.CopyTo(path, compact); return ValueTask.CompletedTask;
	}
	public void Dispose() { _store.Dispose(); if (Directory.Exists(Path)) Directory.Delete(Path, true); }
	public static PageSnapshot Page(string id = "subject", string category = "category") => new(new(id), new("pack", "Main", id), id, "body", ["collection"],
		new("Main", [category], [], true, null), new("source", 1, 1, 1, 1, 1, 1, 1));
	public static PreparedNativePage Prepare(PageSnapshot? page = null, long version = 0, SemanticValue? value = null, bool deleted = false, Guid? mutation = null)
	{
		var p = page ?? Page();
		var scalar = value.GetValueOrDefault();
		SemanticFact[] facts = !value.HasValue ? [] : [new(p.Reference, scalar is PageRef ? "test:p" : scalar is Keyword ? "test:k" : scalar is bool ? "test:b" : "test:i", scalar,
			new("pack", "commit", "path", "field", false))];
		var result = NativePagePreparation.Prepare(Schema, p, facts, version, mutation ?? Guid.NewGuid(), deleted);
		return result is PreparedNativePage prepared ? prepared : throw new InvalidOperationException(result.ToString());
	}
	public static NativeReadBudget Budget(int probes = 8192, long bytes = 2 << 20, long working = 4 << 20)
		=> new(probes, bytes, working, default);
	private sealed class ObservedTx(ITx inner) : ITx, IBoundedReadTx
	{
		internal int Probes { get; private set; }
		internal long Bytes { get; private set; }
		internal long DeltaBytes { get; private set; }
		internal long WorkingBytes { get; private set; }
		private NativeReadBudget? _budget;
		private void Capture(NativeReadBudget budget)
		{
			_budget = budget; Probes = budget.Probes; Bytes = budget.Bytes;
			WorkingBytes = Math.Max(WorkingBytes, budget.WorkingBytes);
		}
		public SharpMUSH.Library.DiscriminatedUnions.Found<NativeReadValue> ReadValue(TableDef table, ReadOnlySpan<byte> key, NativeReadBudget budget)
		{
			try { return ((IBoundedReadTx)inner).ReadValue(table, key, budget); }
			finally { Capture(budget); }
		}
		public NativeReadBatch ReadRange(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, ReadOnlySpan<byte> after, NativeReadBudget budget)
		{
			try { return ((IBoundedReadTx)inner).ReadRange(table, prefix, start, end, after, budget); }
			finally { Capture(budget); }
		}
		public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value) => throw new InvalidOperationException("Unchecked point read used by fixture aggregate.");
		public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		{
			if (_budget is not null) Capture(_budget);
			DeltaBytes += key.Length + value.Length;
			inner.Put(table, key, value);
		}
		public bool Delete(TableDef table, ReadOnlySpan<byte> key)
		{
			if (_budget is not null) Capture(_budget);
			DeltaBytes += key.Length;
			return inner.Delete(table, key);
		}
		public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => inner.Delete(table, key, value);
		public long Count(TableDef table) => throw new InvalidOperationException("Unbounded count used.");
		public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix) => throw new InvalidOperationException("Unchecked range used.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue) => throw new InvalidOperationException("Unchecked range used.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey) => throw new InvalidOperationException("Unchecked range used.");
		public IEnumerable<(byte[] Key, byte[] Value)> RangeReverse(TableDef table, byte[] prefix) => throw new InvalidOperationException("Unchecked range used.");
		public IEnumerable<byte[]> Dups(TableDef table, byte[] key) => throw new InvalidOperationException("Unchecked duplicates used.");
		public SharpMUSH.Library.DiscriminatedUnions.Result<long> CountDups(TableDef table, ReadOnlySpan<byte> key) => throw new InvalidOperationException("Unchecked duplicate count used.");
		public int DeletePrefix(TableDef table, byte[] prefix) => throw new InvalidOperationException("Unbounded delete used.");
	}

}
