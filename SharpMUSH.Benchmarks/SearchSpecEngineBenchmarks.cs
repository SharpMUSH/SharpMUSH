using System.Text.Json;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// <c>@search</c>/<c>lsearch()</c> with COMMAND and LISTEN restrictions (#1468), run by
/// <see cref="SearchSpecEngine"/> over the LMDB provider directly — the mediator forwards the scan and
/// the object lookups to it, and command and listen discovery use the production handlers over LMDB.
/// <para>
/// <see cref="WorldSize"/> grows the scan; <see cref="Selective"/> decides whether one object in a
/// hundred or every object carries the $-command the search asks for; <see cref="ExtraAttributes"/>
/// attributes of <see cref="BodyBytes"/> each sit beside the $-command and ^-listen, so the discovery
/// paths have to scan unrelated attributes. One matching $-command has a nonmatching ^-listen,
/// so the expected result also checks both restrictions are applied; and
/// <see cref="ParentDepth"/> puts every object under a parent chain whose far end holds the attribute
/// an attribute lock (<c>FLAVOR:sweet</c>, resolved through parents as a lock key is) reads. The first candidate
/// overrides it with <c>sour</c> so checking the executor instead of the candidate cannot pass.
/// </para>
/// <para>
/// Besides time and allocations, each case reports what one search read from the attribute tables,
/// from the provider's own counters: <c>attr.val</c> bodies and their bytes, <c>attr.meta</c> rows,
/// and inherited path walks, plus the command/listen discovery requests and the matches kept. A page that
/// is full stops reading when nothing evaluates softcode; a lock keeps every candidate evaluated.
/// </para>
/// </summary>
[BenchmarkCategory("Search", "Lightning")]
[Config(typeof(Config))]
public class SearchSpecEngineBenchmarks
{
	[Params(1_000, 10_000)]
	public int WorldSize { get; set; }

	[Params(true, false)]
	public bool Selective { get; set; }

	[Params(1, 16)]
	public int ExtraAttributes { get; set; }

	[Params(16, 4096)]
	public int BodyBytes { get; set; }

	[Params(0, 4)]
	public int ParentDepth { get; set; }

	private string? _path;
	private LightningDatabase? _database;
	private AnySharpObject? _god;
	private IMediator? _mediator;
	private IAttributeService? _attributes;
	private IBooleanExpressionParser? _locks;
	private IMUSHCodeParser? _parser;
	private ServiceProvider? _services;
	private int _discoveryReads;
	private int _expectedMatches;
	private readonly Dictionary<string, ReadCost> _costs = [];

	private static readonly SearchSpecEngine.SearchPair[] CommandAndListen =
		[new("TYPE", "THING"), new("COMMAND", "+bench frob"), new("LISTEN", "hello there")];

	[GlobalSetup]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-bench-search-" + Guid.NewGuid().ToString("N"));
		_database = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 4L << 30 }, new UnusedPasswordService(), relations: null);
		await _database.Migrate();

		var player = await _database.GetObjectNodeAsync(new DBRef(1)) is AnySharpObject and SharpPlayer god
			? god
			: throw new InvalidOperationException("God (#1) is not seeded as a player.");
		_god = new AnySharpObject(player);
		var room = (await _database.GetObjectNodeAsync(new DBRef(2))) is AnySharpObject { AsOptionalContainer: AnySharpContainer master }
			? master
			: throw new InvalidOperationException("The master room (#2) is not seeded.");

		// head -> ... -> tail; FLAVOR lives on the tail, so a lock key read walks the whole chain.
		DBRef? head = null;
		DBRef? previous = null;
		for (var depth = 0; depth < ParentDepth; depth++)
		{
			var link = await _database.CreateThingAsync($"Ancestor{depth}", room, player, room);
			if (previous is { } child) await _database.SetObjectParent(await Node(child), await Node(link));
			head ??= link;
			previous = link;
		}

		if (previous is { } tail) await _database.SetAttributeAsync(tail, ["FLAVOR"], MarkupText.Plain("sweet"), player);

		var body = MarkupText.Plain(new string('x', BodyBytes));
		for (var i = 0; i < WorldSize; i++)
		{
			var thing = await _database.CreateThingAsync($"Thing{i}", room, player, room);
			var answers = !Selective || i % 100 == 0;
			var hears = i != 100;
			if (answers && hears) _expectedMatches++;
			List<AttributeWrite> writes =
			[
				new(["CMD"], MarkupText.Plain(answers ? "$+bench *:think hit" : "$+other *:think miss"), player, []),
				new(["HEAR"], MarkupText.Plain(hears ? "^*hello*:think heard" : "^*goodbye*:think missed"), player, []),
				.. Enumerable.Range(0, ExtraAttributes).Select(n => new AttributeWrite([$"EXTRA{n:D2}"], body, player, []))
			];
			if (head is null || i == 0)
				writes.Add(new(["FLAVOR"], MarkupText.Plain(i == 0 ? "sour" : "sweet"), player, []));
			await _database.SetAttributesAsync(thing, writes);
			if (head is { } parent) await _database.SetObjectParent(await Node(thing), await Node(parent));
		}

		var database = _database;
		_mediator = Substitute.For<IMediator>();
		_mediator.CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => database.GetFilteredObjectsAsync(call.Arg<GetFilteredObjectsQuery>().Filter!));
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => database.GetObjectNodeAsync(call.Arg<GetObjectNodeQuery>().DBRef));

		// Keep the discovery reads and regex matching real, but bypass the host's Mediator cache so
		// each search measures LMDB reads rather than a precomputed command/listen match list.
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var commands = new GetCommandAttributesQueryHandler(database, options);
		var listens = new GetListenAttributeSnapshotQueryHandler(database, database);
		_mediator.Send(Arg.Any<GetCommandAttributesQuery>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				_discoveryReads++;
				return commands.Handle(call.Arg<GetCommandAttributesQuery>(), call.Arg<CancellationToken>());
			});
		_mediator.Send(Arg.Any<GetListenAttributeSnapshotQuery>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				_discoveryReads++;
				return listens.Handle(call.Arg<GetListenAttributeSnapshotQuery>(), call.Arg<CancellationToken>());
			});

		_services = new ServiceCollection()
			.AddSingleton<IListenPatternMatcher>(new ListenPatternMatcher(_mediator, options))
			.BuildServiceProvider();
		_parser = Substitute.For<IMUSHCodeParser>();
		_parser.ServiceProvider.Returns(_services);

		// @listen only considers the candidate's own attribute. The seeded ^-listeners instead go
		// through the real matcher above.
		_attributes = Substitute.For<IAttributeService>();
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "LISTEN",
				IAttributeService.AttributeMode.Read, parent: false)
			.Returns(async call =>
			{
				var chain = await database.GetAttributeAsync(call.ArgAt<AnySharpObject>(1).Object().DBRef, ["LISTEN"]).ToArrayAsync();
				return chain.Length == 0 ? new OptionalSharpAttributeOrError(new None()) : new OptionalSharpAttributeOrError(chain);
			});

		// SearchSpecEngine passes (executor, candidate); the candidate tries the lock.
		_locks = Substitute.For<IBooleanExpressionParser>();
		_locks.Compile("FLAVOR:sweet").Returns((_, candidate) => FlavorIsSweetAsync(candidate));
	}

	private async ValueTask<bool> FlavorIsSweetAsync(AnySharpObject target)
	{
		await foreach (var hit in _database!.GetAttributeWithInheritanceAsync(target.Object().DBRef, ["FLAVOR"]))
		{
			if (hit.Attributes.LastOrDefault() is { } flavor) return flavor.Value.ToPlainText() == "sweet";
		}

		return false;
	}

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> await _database!.GetObjectNodeAsync(dbref) is AnySharpObject node ? node : throw new InvalidOperationException($"{dbref} is gone");

	[GlobalCleanup]
	public async Task Cleanup()
	{
		ReadCostColumn.Save(this, _costs);
		if (_services is not null) await _services.DisposeAsync();
		if (_database is not null) await _database.DisposeAsync();
		if (_path is not null && Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
	}

	[Benchmark(Description = "COMMAND+LISTEN, COUNT 10")]
	public Task<int> FirstPage() => Run(nameof(FirstPage), Math.Min(10, _expectedMatches), new SearchSpecEngine.SearchPair("COUNT", "10"));

	[Benchmark(Description = "COMMAND+LISTEN, START 6 COUNT 10")]
	public Task<int> SecondPage() => Run(nameof(SecondPage), Math.Clamp(_expectedMatches - 5, 0, 10),
		new SearchSpecEngine.SearchPair("START", "6"), new SearchSpecEngine.SearchPair("COUNT", "10"));

	[Benchmark(Description = "COMMAND+LISTEN, every match")]
	public Task<int> EveryMatch() => Run(nameof(EveryMatch), _expectedMatches);

	[Benchmark(Description = "COMMAND+LISTEN+inherited attribute lock, COUNT 10")]
	public Task<int> InheritedLockPage() => Run(nameof(InheritedLockPage), Math.Clamp(_expectedMatches - 1, 0, 10),
		new SearchSpecEngine.SearchPair("LOCK", "FLAVOR:sweet"), new SearchSpecEngine.SearchPair("COUNT", "10"));

	private async Task<int> Run(string name, int expectedMatches, params SearchSpecEngine.SearchPair[] extra)
	{
		var stats = _database!.ReadStats;
		var (values, bytes, meta, walks) = (stats.ValueReads, stats.ValueBytes, stats.MetaRowReads, stats.PathWalks);
		_discoveryReads = 0;

		var result = await SearchSpecEngine.ExecuteResultAsync(
			_parser!,
			_mediator!,
			Substitute.For<ILocateService>(),
			_attributes!,
			_locks!,
			Substitute.For<IPermissionService>(),
			_god!,
			ownerFilter: null,
			[.. CommandAndListen, .. extra],
			useRegex: false);
		var matches = result is SearchSpecEngine.SearchResult found
			? found.Matches.Count
			: throw new InvalidOperationException("The benchmark's search spec was rejected.");
		if (matches != expectedMatches)
			throw new InvalidOperationException($"{name} expected {expectedMatches} seeded matches, but got {matches}.");

		_costs[name] = new ReadCost(stats.ValueReads - values, stats.ValueBytes - bytes, stats.MetaRowReads - meta,
			stats.PathWalks - walks, _discoveryReads, matches);
		return matches;
	}

	/// <summary>What one search read; the same on every invocation of a case, so the last one is kept.</summary>
	public sealed record ReadCost(long ValueReads, long ValueBytes, long MetaRows, long PathWalks, int DiscoveryReads, int Matches);

	private sealed class Config : AdaptiveBenchmarkConfig
	{
		public Config()
		{
			foreach (var metric in ReadCostColumn.Metrics)
			{
				AddColumn(new ReadCostColumn(metric));
			}
		}
	}

	/// <summary>
	/// One <see cref="ReadCost"/> figure as a summary column. The benchmark runs in its own process, so
	/// that process writes its costs to a file at cleanup, keyed by the case's parameters, and the host
	/// reads them back here when it draws the table.
	/// </summary>
	private sealed class ReadCostColumn(string metric) : IColumn
	{
		public static readonly string[] Metrics =
		[
			nameof(ReadCost.ValueReads), nameof(ReadCost.ValueBytes), nameof(ReadCost.MetaRows),
			nameof(ReadCost.PathWalks), nameof(ReadCost.DiscoveryReads), nameof(ReadCost.Matches)
		];

		private static string CostDirectory => Path.Join(Path.GetTempPath(), "sharpmush-bench-search-costs");

		private static string FileFor(int worldSize, bool selective, int extraAttributes, int bodyBytes, int parentDepth)
			=> Path.Join(CostDirectory, $"{worldSize}-{selective}-{extraAttributes}-{bodyBytes}-{parentDepth}.json");

		public static void Save(SearchSpecEngineBenchmarks benchmark, Dictionary<string, ReadCost> costs)
		{
			Directory.CreateDirectory(CostDirectory);
			var file = FileFor(benchmark.WorldSize, benchmark.Selective, benchmark.ExtraAttributes, benchmark.BodyBytes, benchmark.ParentDepth);
			var merged = File.Exists(file)
				? JsonSerializer.Deserialize<Dictionary<string, ReadCost>>(File.ReadAllText(file)) ?? []
				: [];
			foreach (var (name, cost) in costs) merged[name] = cost;
			File.WriteAllText(file, JsonSerializer.Serialize(merged));
		}

		public string Id => $"{nameof(ReadCostColumn)}.{metric}";
		public string ColumnName => metric;
		public bool AlwaysShow => true;
		public ColumnCategory Category => ColumnCategory.Custom;
		public int PriorityInCategory => Array.IndexOf(Metrics, metric);
		public bool IsNumeric => true;
		public UnitType UnitType => UnitType.Dimensionless;
		public string Legend => $"{metric} per search, from the Lightning provider's attribute read counters";
		public bool IsAvailable(Summary summary) => true;
		public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
		public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);

		public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
		{
			if (benchmarkCase.Parameters[nameof(WorldSize)] is not int worldSize
					|| benchmarkCase.Parameters[nameof(Selective)] is not bool selective
					|| benchmarkCase.Parameters[nameof(ExtraAttributes)] is not int extraAttributes
					|| benchmarkCase.Parameters[nameof(BodyBytes)] is not int bodyBytes
					|| benchmarkCase.Parameters[nameof(ParentDepth)] is not int parentDepth)
			{
				return "?";
			}

			var file = FileFor(worldSize, selective, extraAttributes, bodyBytes, parentDepth);
			if (!File.Exists(file)) return "?";
			var costs = JsonSerializer.Deserialize<Dictionary<string, ReadCost>>(File.ReadAllText(file));
			if (costs is null || !costs.TryGetValue(benchmarkCase.Descriptor.WorkloadMethod.Name, out var cost)) return "?";
			return metric switch
			{
				nameof(ReadCost.ValueReads) => cost.ValueReads.ToString(),
				nameof(ReadCost.ValueBytes) => cost.ValueBytes.ToString(),
				nameof(ReadCost.MetaRows) => cost.MetaRows.ToString(),
				nameof(ReadCost.PathWalks) => cost.PathWalks.ToString(),
				nameof(ReadCost.DiscoveryReads) => cost.DiscoveryReads.ToString(),
				_ => cost.Matches.ToString()
			};
		}
	}

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
