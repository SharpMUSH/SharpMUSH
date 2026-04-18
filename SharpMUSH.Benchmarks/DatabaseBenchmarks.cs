using SharpMUSH.Library.Models;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Database read benchmarks backed by <b>ArangoDB</b>.
/// Measures the raw persistence layer: object lookups, graph traversals, and attribute reads.
/// </summary>
[BenchmarkCategory("Database Read", "ArangoDB")]
public class ArangoDBReadBenchmarks : BaseBenchmark
{
	private AnySharpContainer? _masterRoom;

	public ArangoDBReadBenchmarks()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();
		_masterRoom = _database!.GetObjectNodeAsync(new DBRef(2))
			.ConfigureAwait(false).GetAwaiter().GetResult()
			.Known.AsContainer;
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
/// Database write benchmarks backed by <b>ArangoDB</b>.
/// Each iteration uses a unique name to avoid key collisions.
/// </summary>
[BenchmarkCategory("Database Write", "ArangoDB")]
public class ArangoDBWriteBenchmarks : BaseBenchmark
{
	private SharpPlayer? _godPlayer;
	private AnySharpContainer? _masterRoom;
	private int _counter;

	public ArangoDBWriteBenchmarks()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();
		_godPlayer = _database!.GetObjectNodeAsync(new DBRef(1))
			.ConfigureAwait(false).GetAwaiter().GetResult()
			.AsPlayer;
		_masterRoom = _database!.GetObjectNodeAsync(new DBRef(2))
			.ConfigureAwait(false).GetAwaiter().GetResult()
			.Known.AsContainer;
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
