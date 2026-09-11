using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Shared bodies for the "uncached storage shape" benchmarks: the eight operations the plain
/// per-provider read/write benchmark pairs (<c>LightningReadBenchmarks</c>/<c>LightningWriteBenchmarks</c>,
/// equivalents) don't exercise - inheritance-chain resolution, wildcard/regex attribute
/// listing, a pushed-down filtered search, cascading subtree/object deletes, graph reachability,
/// <c>LightningExtendedBenchmarks</c>) supplies <see cref="Database"/> and does its own provider
/// <c>SurrealBaseBenchmark</c>/<c>LightningBaseBenchmark</c> (single inheritance, and those four
/// share no common ancestor), so each concrete class re-does that one provider's short bootstrap
/// sequence itself and calls <see cref="SeedAsync"/> once it has a live <see cref="Database"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public abstract class ExtendedDatabaseBenchmarks
{
	protected abstract ISharpDatabase Database { get; }

	private SharpPlayer _god = null!;
	private AnySharpContainer _masterRoom = null!;
	private DBRef _inheritanceD;
	private DBRef _wideAttrsObject;
	private DBRef[] _concurrentTargets = [];
	private int _counter;

	/// <summary>
	/// Seeds every fixture the eight benchmarks read from, once per benchmark run. Called by each
	/// concrete class's own <c>[GlobalSetup]</c> after its provider is up.
	/// </summary>
	protected async ValueTask SeedAsync()
	{
		_god = await Database.GetObjectNodeAsync(new DBRef(1)).ConfigureAwait(false) is AnySharpObject and SharpPlayer god
			? god
			: throw new InvalidOperationException("God (#1) is not seeded as a player.");
		_masterRoom = await Database.GetObjectNodeAsync(new DBRef(2)).ConfigureAwait(false) is AnySharpObject { IsContainer: true } masterRoom
			? masterRoom.AsContainer
			: throw new InvalidOperationException("The master room (#2) is not seeded.");

		await SeedInheritanceWalkAsync().ConfigureAwait(false);
		await SeedAttributeListingAsync().ConfigureAwait(false);
		await SeedFilteredSearchAsync().ConfigureAwait(false);
		await SeedReachabilityAsync().ConfigureAwait(false);
		await SeedConcurrentWriteTargetsAsync().ConfigureAwait(false);
	}

	// --- InheritanceWalk -----------------------------------------------------------------------

	/// <summary>
	/// D's parent is C, C's parent is B, B's zone is Z, and DESC is set on Z alone - so resolving
	/// D's DESC walks the whole precedence ladder <c>IAttributeStore.GetAttributeWithInheritanceAsync</c>
	/// documents (self, then the parent chain, then each ancestor's zone) before finding it on the
	/// very last candidate.
	/// </summary>
	private async ValueTask SeedInheritanceWalkAsync()
	{
		var zRef = await Database.CreateThingAsync("ExtBenchZoneZ", _masterRoom, _god, _masterRoom).ConfigureAwait(false);
		var bRef = await Database.CreateThingAsync("ExtBenchThingB", _masterRoom, _god, _masterRoom).ConfigureAwait(false);
		var cRef = await Database.CreateThingAsync("ExtBenchThingC", _masterRoom, _god, _masterRoom).ConfigureAwait(false);
		var dRef = await Database.CreateThingAsync("ExtBenchThingD", _masterRoom, _god, _masterRoom).ConfigureAwait(false);

		if (await Database.GetObjectNodeAsync(zRef).ConfigureAwait(false) is not AnySharpObject z
			|| await Database.GetObjectNodeAsync(bRef).ConfigureAwait(false) is not AnySharpObject b
			|| await Database.GetObjectNodeAsync(cRef).ConfigureAwait(false) is not AnySharpObject c
			|| await Database.GetObjectNodeAsync(dRef).ConfigureAwait(false) is not AnySharpObject d)
			throw new InvalidOperationException("The inheritance-walk fixture objects were not created.");

		await Database.SetObjectZone(b, z).ConfigureAwait(false);
		await Database.SetObjectParent(c, b).ConfigureAwait(false);
		await Database.SetObjectParent(d, c).ConfigureAwait(false);
		await Database.SetAttributeAsync(zRef, ["DESC"], MarkupText.Plain("zone description"), _god).ConfigureAwait(false);

		_inheritanceD = dRef;
	}

	[Benchmark(Description = "GetAttributeWithInheritanceAsync — self, 2 parents, then ancestor zone")]
	public async Task InheritanceWalk()
	{
		await foreach (var _ in Database.GetAttributeWithInheritanceAsync(_inheritanceD, ["DESC"]))
		{ }
	}

	// --- WildcardLattr / RegexLattr --------------------------------------------------------------

	/// <summary>
	/// One object carrying 200 flat attributes (<c>ATTR001</c>..<c>ATTR200</c>) and a 50-leaf
	/// subtree under branch <c>TREE</c> (<c>TREE`1</c>..<c>TREE`50</c>), so a wildcard match on a
	/// slice of the flat attributes and a regex match over the whole subtree can be benchmarked
	/// against the same object.
	/// </summary>
	private async ValueTask SeedAttributeListingAsync()
	{
		var wideRef = await Database.CreateThingAsync("ExtBenchWideAttrs", _masterRoom, _god, _masterRoom).ConfigureAwait(false);

		for (var i = 1; i <= 200; i++)
		{
			await Database.SetAttributeAsync(wideRef, [$"ATTR{i:D3}"], MarkupText.Plain($"v{i}"), _god).ConfigureAwait(false);
		}

		for (var i = 1; i <= 50; i++)
		{
			await Database.SetAttributeAsync(wideRef, ["TREE", $"{i}"], MarkupText.Plain($"v{i}"), _god).ConfigureAwait(false);
		}

		_wideAttrsObject = wideRef;
	}

	[Benchmark(Description = "GetAttributesAsync(ATTR1*) over 200 flat attributes")]
	public async Task WildcardLattr()
	{
		await foreach (var _ in Database.GetAttributesAsync(_wideAttrsObject, "ATTR1*"))
		{ }
	}

	[Benchmark(Description = "GetAttributesByRegexAsync(^TREE.*) over a 50-leaf subtree")]
	public async Task RegexLattr()
	{
		await foreach (var _ in Database.GetAttributesByRegexAsync(_wideAttrsObject, "^TREE.*"))
		{ }
	}

	// --- FilteredSearch --------------------------------------------------------------------------

	/// <summary>
	/// 1,000 things, ownership alternating between God and a second player, with WIZARD set on
	/// every tenth (all falling on the God-owned half, since 10 is even) - so
	/// <c>Types + HasFlag + Owner</c> together narrow 1,000 candidates down to exactly 100 matches
	/// at the database level.
	/// </summary>
	private async ValueTask SeedFilteredSearchAsync()
	{
		var secondPlayerRef = await Database.CreatePlayerAsync(
			"ExtBenchSecondPlayer", "bench-pw", _masterRoom.Object().DBRef, _masterRoom.Object().DBRef, 0).ConfigureAwait(false);
		if (await Database.GetObjectNodeAsync(secondPlayerRef).ConfigureAwait(false) is not (AnySharpObject and SharpPlayer secondPlayer))
			throw new InvalidOperationException("The second filtered-search player was not created.");
		var wizardFlag = await Database.GetObjectFlagAsync("WIZARD").ConfigureAwait(false)
			?? throw new InvalidOperationException("WIZARD flag is not seeded - migration did not run.");

		for (var i = 0; i < 1000; i++)
		{
			var owner = i % 2 == 0 ? _god : secondPlayer;
			var thingRef = await Database.CreateThingAsync($"ExtBenchFiltered{i:D4}", _masterRoom, owner, _masterRoom).ConfigureAwait(false);

			if (i % 10 == 0)
			{
				if (await Database.GetObjectNodeAsync(thingRef).ConfigureAwait(false) is not AnySharpObject thing)
					throw new InvalidOperationException($"Filtered-search thing {thingRef} was not created.");
				await Database.SetObjectFlagAsync(thing, wizardFlag).ConfigureAwait(false);
			}
		}
	}

	[Benchmark(Description = "GetFilteredObjectsAsync(Types=THING, HasFlag=WIZARD, Owner=God) over 1,000 things")]
	public async Task FilteredSearch()
	{
		var filter = new ObjectSearchFilter
		{
			Types = ["THING"],
			HasFlag = "WIZARD",
			Owner = new DBRef(1)
		};

		await foreach (var _ in Database.GetFilteredObjectsAsync(filter))
		{ }
	}

	// --- WipeSubtree -----------------------------------------------------------------------------

	private DBRef _wipeTarget;

	[IterationSetup(Target = nameof(WipeSubtree))]
	public void SeedWipeSubtree()
	{
		_wipeTarget = SeedWipeSubtreeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>Creates a fresh object with a 50-leaf subtree under branch <c>SUB</c> to wipe.</summary>
	private async ValueTask<DBRef> SeedWipeSubtreeAsync()
	{
		var name = $"ExtBenchWipeTarget{Interlocked.Increment(ref _counter):X8}";
		var dbref = await Database.CreateThingAsync(name, _masterRoom, _god, _masterRoom).ConfigureAwait(false);

		for (var i = 1; i <= 50; i++)
		{
			await Database.SetAttributeAsync(dbref, ["SUB", $"{i}"], MarkupText.Plain($"v{i}"), _god).ConfigureAwait(false);
		}

		return dbref;
	}

	[Benchmark(Description = "WipeAttributeAsync over a freshly-created 50-leaf subtree")]
	public async Task<bool> WipeSubtree() =>
		await Database.WipeAttributeAsync(_wipeTarget, ["SUB"]);

	// --- DeleteObject ----------------------------------------------------------------------------

	private DBRef _deleteTarget;

	[IterationSetup(Target = nameof(DeleteObject))]
	public void SeedDeleteObject()
	{
		_deleteTarget = SeedDeleteObjectAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>Creates a fresh thing carrying 20 attributes to delete.</summary>
	private async ValueTask<DBRef> SeedDeleteObjectAsync()
	{
		var name = $"ExtBenchDeleteTarget{Interlocked.Increment(ref _counter):X8}";
		var dbref = await Database.CreateThingAsync(name, _masterRoom, _god, _masterRoom).ConfigureAwait(false);

		for (var i = 1; i <= 20; i++)
		{
			await Database.SetAttributeAsync(dbref, [$"ATTR{i:D2}"], MarkupText.Plain($"v{i}"), _god).ConfigureAwait(false);
		}

		return dbref;
	}

	[Benchmark(Description = "DeleteObjectAsync on a freshly-created thing with 20 attributes")]
	public async Task<bool> DeleteObject() =>
		await Database.DeleteObjectAsync(_deleteTarget);

	// --- Reachability ----------------------------------------------------------------------------

	private AnySharpObject _reachabilityHead = null!;
	private AnySharpObject _reachabilityTail = null!;

	/// <summary>A 30-object parent chain: <c>chain[0]</c>'s parent is <c>chain[1]</c>, and so on up
	/// to <c>chain[29]</c>, so reaching the tail from the head walks the whole chain.</summary>
	private async ValueTask SeedReachabilityAsync()
	{
		const int chainLength = 30;
		var nodes = new AnySharpObject[chainLength];

		for (var i = 0; i < chainLength; i++)
		{
			var dbref = await Database.CreateThingAsync($"ExtBenchChain{i:D2}", _masterRoom, _god, _masterRoom).ConfigureAwait(false);
			nodes[i] = await Database.GetObjectNodeAsync(dbref).ConfigureAwait(false) is AnySharpObject node
				? node
				: throw new InvalidOperationException($"Reachability chain object {dbref} was not created.");
		}

		for (var i = 0; i < chainLength - 1; i++)
		{
			await Database.SetObjectParent(nodes[i], nodes[i + 1]).ConfigureAwait(false);
		}

		_reachabilityHead = nodes[0];
		_reachabilityTail = nodes[chainLength - 1];
	}

	[Benchmark(Description = "IsReachableViaParentOrZoneAsync across a 30-deep parent chain")]
	public async Task<bool> Reachability() =>
		await Database.IsReachableViaParentOrZoneAsync(_reachabilityHead, _reachabilityTail, 100);

	// --- ConcurrentAttributeWrites ----------------------------------------------------------------

	private const int ConcurrentWriterCount = 8;
	private const int WritesPerWorker = 25;

	/// <summary>Eight objects, one per concurrent writer, created once so the benchmark measures
	/// write contention rather than object creation.</summary>
	private async ValueTask SeedConcurrentWriteTargetsAsync()
	{
		var targets = new DBRef[ConcurrentWriterCount];

		for (var i = 0; i < ConcurrentWriterCount; i++)
		{
			targets[i] = await Database.CreateThingAsync($"ExtBenchConcurrent{i}", _masterRoom, _god, _masterRoom).ConfigureAwait(false);
		}

		_concurrentTargets = targets;
	}

	[Benchmark(Description = "8 concurrent writers x 25 SetAttributeAsync calls each, on separate objects")]
	public async Task ConcurrentAttributeWrites()
	{
		var tasks = _concurrentTargets.Select(target => Task.Run(async () =>
		{
			for (var i = 0; i < WritesPerWorker; i++)
			{
				await Database.SetAttributeAsync(
					target,
					["CONCURRENT_ATTR"],
					MarkupText.Plain($"v{Interlocked.Increment(ref _counter)}"),
					_god).ConfigureAwait(false);
			}
		}));

		await Task.WhenAll(tasks);
	}
}
