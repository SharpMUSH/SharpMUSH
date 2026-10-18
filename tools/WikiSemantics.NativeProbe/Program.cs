using System.Text.Json;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Tools.WikiSemantics;
using SharpMUSH.Tools.WikiSemantics.Native;
using SharpMUSH.Library.Models.Wiki.Semantics;

// Disposable test process only; no production registration, arbitrary fault hooks or native-file edits.
var options = new Dictionary<string, string>(StringComparer.Ordinal);
if (args.Length % 2 != 0) throw new ArgumentException("option/value pairs required");
for (var i = 0; i < args.Length; i += 2)
	if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate option");
if (options.Keys.Any(k => k is not ("--scenario" or "--world" or "--evidence" or "--fixture-id"))) throw new ArgumentException("Unknown option");
var scenario = options["--scenario"];
var world = Path.GetFullPath(options["--world"]);
var evidence = Path.GetFullPath(options["--evidence"]);
if (world == evidence || evidence.StartsWith(world + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Evidence must be outside world");
if (scenario != "cipher-cursor-error") Directory.CreateDirectory(evidence);
using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
void Mark(string phase, Guid mutation = default)
{
	Console.WriteLine(JsonSerializer.Serialize(new { phase, pid = Environment.ProcessId, scenario, world, mutation }));
	Console.Out.Flush();
}
if (scenario == "cipher-cursor-error")
{
	if (options.ContainsKey("--fixture-id")) throw new ArgumentException("Cipher probe requires a new owned world");
	CipherCursorErrorScenario.Run(world, evidence, phase => Mark(phase));
	return;
}
if (scenario == "persisted-flags")
{
	if (!Guid.TryParse(options.GetValueOrDefault("--fixture-id"), out var fixtureId)) throw new ArgumentException("Fixture ID required");
	using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "fixture.json")));
	var ownership = marker.RootElement;
	if (ownership.GetProperty("world").GetString() != world || ownership.GetProperty("fixtureId").GetGuid() != fixtureId
		|| ownership.GetProperty("parentPid").GetInt32() == Environment.ProcessId) throw new InvalidDataException("Ownership mismatch");
	if (!File.Exists(Path.Combine(world, "data.mdb"))) throw new InvalidDataException("Missing owned fixture");
	using var storage = new ProbeStorage(world);
	var table = storage.OpenTable("prototype.semantic.recovery.duplicates", false);
	var budget = new NativeReadBudget(16, 4096, 8192, default);
	try
	{
		storage.Read(tx => ((IBoundedReadTx)tx).ReadRange(table, [], [], [], [], budget)).Dispose();
		throw new InvalidOperationException("Persisted duplicate flags were ignored");
	}
	catch (NotSupportedException)
	{
		if (budget.Probes != 0) throw new InvalidOperationException("Rejection occurred after native probing");
		Mark("persisted-flags-rejected");
		return;
	}
}
if (scenario is not ("crash-before-commit" or "crash-after-commit")) throw new ArgumentException("Unsupported scenario");
if (options.ContainsKey("--fixture-id") || Directory.Exists(world) || File.Exists(world)) throw new InvalidOperationException("Crash probe requires a new world");
using (var storage = new ProbeStorage(world))
{
	var schema = new SemanticSchema(1, [new("test:i", 1, SemanticType.Integer, Cardinality.Multiple, []),
		new("test:p", 1, SemanticType.PageRef, Cardinality.Multiple, []), new("test:k", 1, SemanticType.Keyword, Cardinality.Multiple, []), new("test:b", 1, SemanticType.Boolean, Cardinality.Single, [])]);
	var store = new NativeFixtureStore(storage, schema);
	var page = new PageSnapshot(new("subject"), new("pack", "Main", "subject"), "subject", "body", ["collection"],
		new("Main", ["category"], [], true, null), new("source", 1, 1, 1, 1, 1, 1, 1));
	PreparedNativePage Prepare(long version, long value, Guid mutation)
	{
		SemanticFact[] facts = [new(page.Reference, "test:i", value, new("pack", "commit", "path", "field", false))];
		return NativePagePreparation.Prepare(schema, page, facts, version, mutation, false) is PreparedNativePage edit
			? edit : throw new InvalidOperationException("Preparation rejected");
	}
	if (await store.CommitAsync(Prepare(0, 1, Guid.NewGuid())) is not NativeCommitted) throw new InvalidOperationException("Seed failed");
	File.WriteAllText(Path.Combine(evidence, "before.txt"), Snapshot(storage));
	var mutation = Guid.NewGuid();
	File.WriteAllText(Path.Combine(evidence, "mutation.txt"), mutation.ToString());
	Mark("seeded", mutation);
	if (scenario == "crash-before-commit")
	{
		await storage.WriteAsync<bool>(tx =>
		{
			if (store.Apply(tx, Prepare(1, 2, mutation), default) is not NativeCommitted) throw new InvalidOperationException("Apply rejected");
			Mark("applied-uncommitted", mutation);
			Thread.Sleep(TimeSpan.FromSeconds(80));
			throw new TimeoutException("Parent failed to terminate owned probe");
		});
	}
	else
	{
		if (await store.CommitAsync(Prepare(1, 2, mutation)) is not NativeCommitted { StateVersion: 2, CommitSequence: 2 }) throw new InvalidOperationException("Commit rejected");
		File.WriteAllText(Path.Combine(evidence, "after.txt"), Snapshot(storage));
		Mark("committed", mutation);
		await Task.Delay(TimeSpan.FromSeconds(80));
		throw new TimeoutException("Parent failed to terminate owned probe");
	}
}

static string Snapshot(ProbeStorage storage)
{
	var lines = new List<string>();
	foreach (var suffix in new[] { "metadata", "registry", "pages", "summaries", "facts", "forward", "postings", "catalog", "partitions", "mutations" })
	{
		var table = storage.OpenTable("prototype.semantic.b1." + suffix, false);
		byte[] after = [];
		while (true)
		{
			using var batch = storage.Read(tx => ((IBoundedReadTx)tx).ReadRange(table, [], [], [], after, new(8192, 2 << 20, 4 << 20, default)));
			foreach (var row in batch.Entries) lines.Add(table.Name + ":" + Convert.ToHexString(row.Key) + "=" + Convert.ToHexString(row.Value));
			if (batch.Exhausted) break;
			after = batch.Entries[^1].Key.ToArray();
		}
	}
	return string.Join('\n', lines);
}

sealed class ProbeStorage(string path) : ILightningStorageAccessor, IDisposable
{
	private readonly LightningStore _store = new(new LightningStoreOptions { Path = path, MapSize = 256L << 20, Sync = LightningSyncMode.Full });
	public T Read<T>(Func<ITx, T> read) => _store.Read(read);
	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _store.WriteAsync(job, ct);
	public TableDef OpenTable(string name, bool duplicates) => _store.OpenTable(name, duplicates);
	public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default) => _store.RangeAsync(table, prefix, pageSize, ct);
	public ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _store.CopyTo(path, compact); return ValueTask.CompletedTask; }
	public void Dispose() => _store.Dispose();
}
