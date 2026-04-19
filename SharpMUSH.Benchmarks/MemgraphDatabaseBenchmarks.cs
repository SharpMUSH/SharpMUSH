namespace SharpMUSH.Benchmarks;

/// <summary>
/// Database read benchmarks backed by <b>Memgraph</b>.
/// Mirrors <see cref="ArangoDBReadBenchmarks"/> — compare results to quantify backend differences.
/// </summary>
[BenchmarkCategory("Database Read", "Memgraph")]
public class MemgraphReadBenchmarks : MemgraphBaseBenchmark
{
	private AnySharpContainer? _masterRoom;

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_masterRoom = (await _database!.GetObjectNodeAsync(new DBRef(2)).ConfigureAwait(false)).Known.AsContainer;
	}

	[Benchmark(Description = "GetObjectNodeAsync(#1) — God player")]
	public async ValueTask<AnyOptionalSharpObject> GetGodPlayerNode() =>
		await _database!.GetObjectNodeAsync(new DBRef(1));

	[Benchmark(Description = "GetObjectNodeAsync(#2) — Master Room")]
	public async ValueTask<AnyOptionalSharpObject> GetMasterRoomNode() =>
		await _database!.GetObjectNodeAsync(new DBRef(2));

	[Benchmark(Description = "GetContentsAsync(Master Room)")]
	public async Task GetRoomContents()
	{
		await foreach (var _ in _database!.GetContentsAsync(_masterRoom!))
		{ /* enumerate */ }
	}

	[Benchmark(Description = "GetLocationAsync(#1) — 1-hop traversal")]
	public async ValueTask<AnyOptionalSharpContainer> GetLocation() =>
		await _database!.GetLocationAsync(new DBRef(1));

	[Benchmark(Description = "GetAttributeAsync(#1, AADESC)")]
	public async Task GetAttribute()
	{
		await foreach (var _ in _database!.GetAttributeAsync(new DBRef(1), ["AADESC"]))
		{ /* enumerate */ }
	}
}

/// <summary>
/// Database write benchmarks backed by <b>Memgraph</b>.
/// Mirrors <see cref="ArangoDBWriteBenchmarks"/> — compare results to quantify backend differences.
/// </summary>
[BenchmarkCategory("Database Write", "Memgraph")]
public class MemgraphWriteBenchmarks : MemgraphBaseBenchmark
{
	private SharpPlayer? _godPlayer;
	private AnySharpContainer? _masterRoom;
	private int _counter;

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_godPlayer = (await _database!.GetObjectNodeAsync(new DBRef(1)).ConfigureAwait(false)).AsPlayer;
		_masterRoom = (await _database!.GetObjectNodeAsync(new DBRef(2)).ConfigureAwait(false)).Known.AsContainer;
	}

	[Benchmark(Description = "CreateThingAsync — unique name each call")]
	public async ValueTask<DBRef> CreateThing()
	{
		var name = $"bench_{Interlocked.Increment(ref _counter):X8}";
		return await _database!.CreateThingAsync(name, _masterRoom!, _godPlayer!, _masterRoom!);
	}

	[Benchmark(Description = "SetAttributeAsync on #1")]
	public async ValueTask<bool> SetAttribute() =>
		await _database!.SetAttributeAsync(
			new DBRef(1),
			["BENCH_ATTR"],
			MModule.single($"v{Interlocked.Increment(ref _counter)}"),
			_godPlayer!);
}
