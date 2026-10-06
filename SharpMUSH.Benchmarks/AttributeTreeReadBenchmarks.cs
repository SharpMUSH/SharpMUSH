using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The attribute-tree reads of #1466, #1467 and #1471 against the LMDB provider directly — no host, no
/// cache, no NATS — so what is measured is the provider's own read path, cold to the engine's cache.
/// The tree is grown on three independent axes: <see cref="Grandchildren"/> under each direct child
/// (what a direct-child or top-level read must not pay for), <see cref="BodyBytes"/> per value (what a
/// name listing must not pay for) and <see cref="ParentDepth"/> (how far an inherited read could walk,
/// with a zone on every object of the chain, though a hit on the nearest parent should walk no further).
/// </summary>
[BenchmarkCategory("Attribute Tree", "Lightning")]
public class AttributeTreeReadBenchmarks
{
	private const int Children = 16;

	[Params(0, 1000)]
	public int Grandchildren { get; set; }

	[Params(16, 4096)]
	public int BodyBytes { get; set; }

	[Params(1, 8)]
	public int ParentDepth { get; set; }

	private string? _path;
	private LightningDatabase? _database;
	private DBRef _tree;
	private DBRef _child;

	[GlobalSetup]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-bench-tree-" + Guid.NewGuid().ToString("N"));
		_database = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 4L << 30 }, new UnusedPasswordService(), relations: null);
		await _database.Migrate();

		var god = (await _database.GetObjectNodeAsync(new DBRef(1))) is AnySharpObject and SharpPlayer player
			? player
			: throw new InvalidOperationException("God (#1) is not seeded as a player.");
		var room = (await _database.GetObjectNodeAsync(new DBRef(2))) is AnySharpObject { IsContainer: true } master
			? master.AsContainer
			: throw new InvalidOperationException("The master room (#2) is not seeded.");

		var body = MarkupText.Plain(new string('x', BodyBytes));
		_tree = await _database.CreateThingAsync("Tree", room, god, room);
		var writes = new List<AttributeWrite>();
		for (var c = 0; c < Children; c++)
		{
			writes.Add(new AttributeWrite(["ROOT", $"C{c:D2}"], body, god, []));
			writes.Add(new AttributeWrite([$"TOP{c:D2}"], body, god, []));
			for (var g = 0; g < Grandchildren; g++)
			{
				writes.Add(new AttributeWrite(["ROOT", $"C{c:D2}", $"G{g:D4}"], body, god, []));
				writes.Add(new AttributeWrite([$"TOP{c:D2}", $"G{g:D4}"], body, god, []));
			}
		}

		foreach (var batch in writes.Chunk(2048))
		{
			await _database.SetAttributesAsync(_tree, batch);
		}

		// child -> p1 -> ... -> pN, each with a zone; the leaf lives on p1 and on every zone.
		_child = await _database.CreateThingAsync("Child", room, god, room);
		var previous = _child;
		for (var depth = 0; depth < ParentDepth; depth++)
		{
			var parent = await _database.CreateThingAsync($"Parent{depth}", room, god, room);
			var zone = await _database.CreateThingAsync($"Zone{depth}", room, god, room);
			await _database.SetObjectParent(await Node(previous), await Node(parent));
			await _database.SetObjectZone(await Node(previous), await Node(zone));
			await _database.SetAttributeAsync(zone, ["ROOT", "BRANCH", "LEAF"], body, god);
			if (depth == 0)
			{
				await _database.SetAttributeAsync(parent, ["ROOT", "BRANCH", "LEAF"], body, god);
			}

			previous = parent;
		}
	}

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> await _database!.GetObjectNodeAsync(dbref) is AnySharpObject node ? node : throw new InvalidOperationException($"{dbref} is gone");

	[GlobalCleanup]
	public async Task Cleanup()
	{
		if (_database is not null) await _database.DisposeAsync();
		if (_path is not null && Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
	}

	[Benchmark(Description = "Top-level attributes (lazy)")]
	public async Task<int> TopLevel()
		=> await (await Node(_tree)).Object().LazyAttributes.Value.CountAsync();

	[Benchmark(Description = "Direct children of ROOT (lazy)")]
	public async Task<int> DirectChildrenLazy()
	{
		var root = await _database!.GetLazyAttributeAsync(_tree, ["ROOT"]).LastAsync();
		return await (await root.Leaves.WithCancellation(CancellationToken.None)).CountAsync();
	}

	[Benchmark(Description = "Direct children of ROOT (eager, with values)")]
	public async Task<int> DirectChildrenEager()
	{
		var root = await _database!.GetAttributeAsync(_tree, ["ROOT"]).LastAsync();
		return await (await root.Leaves.WithCancellation(CancellationToken.None)).CountAsync();
	}

	[Benchmark(Description = "Pattern ROOT`** names (lazy)")]
	public async Task<int> PatternNamesLazy()
		=> await _database!.GetLazyAttributesAsync(_tree, "ROOT`**").Select(x => x.LongName).CountAsync();

	[Benchmark(Description = "Pattern ROOT`** (eager, with values)")]
	public async Task<int> PatternEager()
		=> await _database!.GetAttributesAsync(_tree, "ROOT`**").CountAsync();

	[Benchmark(Description = "Inherited ROOT`BRANCH`LEAF, nearest-parent hit (eager)")]
	public async Task<int> InheritedNearestParent()
		=> await _database!.GetAttributeWithInheritanceAsync(_child, ["ROOT", "BRANCH", "LEAF"]).CountAsync();

	[Benchmark(Description = "Inherited MISSING, full parent and zone walk (eager)")]
	public async Task<int> InheritedMiss()
		=> await _database!.GetAttributeWithInheritanceAsync(_child, ["ROOT", "MISSING", "LEAF"]).CountAsync();

	/// <summary>The provider hashes a password only for a player it creates; these benchmarks create things.</summary>
	private sealed class UnusedPasswordService : IPasswordService
	{
		public string HashPassword(string pw) => throw new NotSupportedException();
		public bool PasswordIsValid(string pw, string hash) => throw new NotSupportedException();
		public ValueTask SetPassword(SharpPlayer user, string hashedPassword) => throw new NotSupportedException();
		public string GenerateRandomPassword() => throw new NotSupportedException();
		public bool NeedsRehash(string hash) => throw new NotSupportedException();
		public ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext) => throw new NotSupportedException();
	}
}
