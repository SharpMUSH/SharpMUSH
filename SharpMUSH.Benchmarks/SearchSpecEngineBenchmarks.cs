using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// <c>@search</c>/<c>lsearch()</c> with COMMAND and LISTEN restrictions (#1468), run by
/// <see cref="SearchSpecEngine"/> over the LMDB provider directly — the mediator forwards the scan and
/// the object lookups to it, and visible attributes are each object's own, read as God sees them.
/// <see cref="WorldSize"/> grows the scan; <see cref="Selective"/> decides whether one object in a
/// hundred or every object carries the $-command the search asks for. Each benchmark returns the
/// number of visible-attribute reads it made, the work the page is meant to bound: a 10-match page
/// stops reading once it is full, a full listing reads every candidate.
/// </summary>
[BenchmarkCategory("Search", "Lightning")]
[Config(typeof(AdaptiveBenchmarkConfig))]
public class SearchSpecEngineBenchmarks
{
	[Params(1_000, 10_000)]
	public int WorldSize { get; set; }

	[Params(true, false)]
	public bool Selective { get; set; }

	private string? _path;
	private LightningDatabase? _database;
	private AnySharpObject? _god;
	private IMediator? _mediator;
	private IAttributeService? _attributes;
	private int _reads;

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
		var room = (await _database.GetObjectNodeAsync(new DBRef(2))) is AnySharpObject { IsContainer: true } master
			? master.AsContainer
			: throw new InvalidOperationException("The master room (#2) is not seeded.");

		for (var i = 0; i < WorldSize; i++)
		{
			var thing = await _database.CreateThingAsync($"Thing{i}", room, player, room);
			var answers = !Selective || i % 100 == 0;
			await _database.SetAttributesAsync(thing,
			[
				new AttributeWrite(["CMD"], MarkupText.Plain(answers ? "$+bench *:think hit" : "$+other *:think miss"), player, []),
				new AttributeWrite(["HEAR"], MarkupText.Plain("^*hello*:think heard"), player, []),
				new AttributeWrite(["DESC"], MarkupText.Plain(new string('x', 256)), player, [])
			]);
		}

		var database = _database;
		_mediator = Substitute.For<IMediator>();
		_mediator.CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => database.GetFilteredObjectsAsync(call.Arg<GetFilteredObjectsQuery>().Filter!));
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => database.GetObjectNodeAsync(call.Arg<GetObjectNodeQuery>().DBRef));

		// Every top-level attribute of the object, as God sees them, counting each read.
		_attributes = Substitute.For<IAttributeService>();
		_attributes.GetVisibleAttributesAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<int>())
			.Returns(call =>
			{
				_reads++;
				return ReadAllAsync(call.ArgAt<AnySharpObject>(1));
			});
	}

	[GlobalCleanup]
	public async Task Cleanup()
	{
		if (_database is not null) await _database.DisposeAsync();
		if (_path is not null && Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
	}

	[Benchmark(Description = "COMMAND+LISTEN, COUNT 10")]
	public Task<int> FirstPage() => Run(new SearchSpecEngine.SearchPair("COUNT", "10"));

	[Benchmark(Description = "COMMAND+LISTEN, START 5 COUNT 10")]
	public Task<int> SecondPage() => Run(new SearchSpecEngine.SearchPair("START", "5"), new SearchSpecEngine.SearchPair("COUNT", "10"));

	[Benchmark(Description = "COMMAND+LISTEN, every match")]
	public Task<int> EveryMatch() => Run();

	private async Task<int> Run(params SearchSpecEngine.SearchPair[] page)
	{
		_reads = 0;
		await SearchSpecEngine.ExecuteResultAsync(
			Substitute.For<IMUSHCodeParser>(),
			_mediator!,
			Substitute.For<ILocateService>(),
			_attributes!,
			Substitute.For<IBooleanExpressionParser>(),
			Substitute.For<IPermissionService>(),
			_god!,
			ownerFilter: null,
			[.. CommandAndListen, .. page],
			useRegex: false);
		return _reads;
	}

	private static async ValueTask<SharpAttributesOrError> ReadAllAsync(AnySharpObject obj)
		=> await obj.Object().Attributes.Value.ToArrayAsync();

	/// <summary>The provider hashes a password only for a player it creates; these benchmarks create things.</summary>
	private sealed class UnusedPasswordService : IPasswordService
	{
		public string HashPassword(string user, string pw) => throw new NotSupportedException();
		public bool PasswordIsValid(string user, string pw, string hash) => throw new NotSupportedException();
		public ValueTask SetPassword(SharpPlayer user, string hashedPassword) => throw new NotSupportedException();
		public string GenerateRandomPassword() => throw new NotSupportedException();
		public bool NeedsRehash(string hash) => throw new NotSupportedException();
		public ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext) => throw new NotSupportedException();
	}
}
