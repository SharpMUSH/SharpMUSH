using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks that directly compare the old N+1 scan pattern against the new
/// <see cref="GetAllTypedObjectsQuery"/> introduced to eliminate FusionCache
/// per-key SemaphoreSlim contention during full-database scans.
///
/// The old pattern was:
/// <code>
///   await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery()))
///       await mediator.Send(new GetObjectNodeQuery(obj.DBRef));   // N cache lookups
/// </code>
/// The new pattern is:
/// <code>
///   await foreach (var obj in mediator.CreateStream(new GetAllTypedObjectsQuery()))
///       // obj is already AnySharpObject — zero extra round-trips
/// </code>
/// </summary>
[BenchmarkCategory("Full-DB Scan", "ArangoDB")]
public class ArangoScanBenchmarks : BaseBenchmark
{
	private IMediator? _mediator;

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_mediator = _server!.Services.GetRequiredService<IMediator>();

		// Seed a handful of objects so the scan is non-trivial.
		var god = (await _database!.GetObjectNodeAsync(new DBRef(1)).ConfigureAwait(false)).AsPlayer;
		var room = (await _database!.GetObjectNodeAsync(new DBRef(2)).ConfigureAwait(false)).Known.AsContainer;
		for (var i = 0; i < 10; i++)
			await _database!.CreateThingAsync($"ScanThing_{i}", room, god!, room).ConfigureAwait(false);
	}

	/// <summary>
	/// Baseline: stream all objects only (no secondary lookup).
	/// </summary>
	[Benchmark(Baseline = true, Description = "GetAllObjectsQuery — stream base objects only")]
	public async Task<int> StreamBaseObjects()
	{
		var count = 0;
		await foreach (var _ in _mediator!.CreateStream(new GetAllObjectsQuery()))
			count++;
		return count;
	}

	/// <summary>
	/// Old (buggy) pattern: stream base objects then issue a per-object
	/// <c>GetObjectNodeQuery</c> through the cache — the N+1 pattern that
	/// causes FusionCache lock contention with concurrent player commands.
	/// </summary>
	[Benchmark(Description = "OLD: GetAllObjectsQuery + GetObjectNodeQuery per object (N+1 cache hits)")]
	public async Task<int> OldPattern_StreamThenLookupEach()
	{
		var count = 0;
		await foreach (var obj in _mediator!.CreateStream(new GetAllObjectsQuery()))
		{
			var typed = await _mediator.Send(new GetObjectNodeQuery(obj.DBRef));
			if (!typed.IsNone)
				count++;
		}
		return count;
	}

	/// <summary>
	/// New (fixed) pattern: stream fully-typed objects directly — bypasses the
	/// per-object FusionCache lock entirely.
	/// </summary>
	[Benchmark(Description = "NEW: GetAllTypedObjectsQuery — zero secondary cache lookups")]
	public async Task<int> NewPattern_StreamTypedObjects()
	{
		var count = 0;
		await foreach (var _ in _mediator!.CreateStream(new GetAllTypedObjectsQuery()))
			count++;
		return count;
	}
}

/// <summary>
/// Memgraph mirror of <see cref="ArangoScanBenchmarks"/>.
/// </summary>
[BenchmarkCategory("Full-DB Scan", "Memgraph")]
public class MemgraphScanBenchmarks : MemgraphBaseBenchmark
{
	private IMediator? _mediator;

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_mediator = _server!.Services.GetRequiredService<IMediator>();

		var god = (await _database!.GetObjectNodeAsync(new DBRef(1)).ConfigureAwait(false)).AsPlayer;
		var room = (await _database!.GetObjectNodeAsync(new DBRef(2)).ConfigureAwait(false)).Known.AsContainer;
		for (var i = 0; i < 10; i++)
			await _database!.CreateThingAsync($"ScanThing_{i}", room, god!, room).ConfigureAwait(false);
	}

	[Benchmark(Baseline = true, Description = "GetAllObjectsQuery — stream base objects only")]
	public async Task<int> StreamBaseObjects()
	{
		var count = 0;
		await foreach (var _ in _mediator!.CreateStream(new GetAllObjectsQuery()))
			count++;
		return count;
	}

	[Benchmark(Description = "OLD: GetAllObjectsQuery + GetObjectNodeQuery per object (N+1 cache hits)")]
	public async Task<int> OldPattern_StreamThenLookupEach()
	{
		var count = 0;
		await foreach (var obj in _mediator!.CreateStream(new GetAllObjectsQuery()))
		{
			var typed = await _mediator.Send(new GetObjectNodeQuery(obj.DBRef));
			if (!typed.IsNone)
				count++;
		}
		return count;
	}

	[Benchmark(Description = "NEW: GetAllTypedObjectsQuery — zero secondary cache lookups")]
	public async Task<int> NewPattern_StreamTypedObjects()
	{
		var count = 0;
		await foreach (var _ in _mediator!.CreateStream(new GetAllTypedObjectsQuery()))
			count++;
		return count;
	}
}
