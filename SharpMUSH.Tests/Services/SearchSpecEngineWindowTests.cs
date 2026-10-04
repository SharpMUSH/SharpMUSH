using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The per-object path of <see cref="SearchSpecEngine"/> (#1468): a search with both COMMAND and LISTEN
/// reads each candidate's visible attributes once; a search carrying softcode (a lock or an EVAL)
/// evaluates every candidate whatever page is asked for, one without stops once its page is full; and
/// START/COUNT give the same page they gave when the whole match list was built first, keeping only it.
/// </summary>
public class SearchSpecEngineWindowTests
{
	private const int Candidates = 10;

	private static readonly SharpObjectFlag Wizard = new()
	{
		Name = "WIZARD",
		Symbol = "W",
		System = true,
		SetPermissions = [],
		UnsetPermissions = [],
		TypeRestrictions = []
	};

	private static AnySharpObject Thing(int key, params SharpObjectFlag[] flags)
		=> new(new SharpThing
		{
			Object = new SharpObject
			{
				Key = key,
				CreationTime = 1000L + key,
				Name = $"Thing{key}",
				Type = "THING",
				Locks = ImmutableDictionary<string, SharpLockData>.Empty,
				Owner = new(async _ => { await ValueTask.CompletedTask; return null!; }),
				Powers = new(AsyncEnumerable.Empty<SharpPower>),
				Attributes = new(AsyncEnumerable.Empty<SharpAttribute>),
				LazyAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
				AllAttributes = new(AsyncEnumerable.Empty<SharpAttribute>),
				LazyAllAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
				Flags = new(() => flags.ToAsyncEnumerable()),
				Parent = new(async _ => { await ValueTask.CompletedTask; return new AnyOptionalSharpObject(new None()); }),
				Zone = new(async _ => { await ValueTask.CompletedTask; return new AnyOptionalSharpObject(new None()); }),
				Children = new(AsyncEnumerable.Empty<SharpObject>)
			},
			Location = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async _ => { await ValueTask.CompletedTask; return null!; })
		});

	private static SharpAttribute Attribute(string name, string value)
	{
		var attribute = TestAttributeFactory.Named(name);
		attribute.Value = MarkupText.Plain(value);
		return attribute;
	}

	/// <summary>Runs the search over <see cref="Candidates"/> things, every odd one of which has the
	/// $-command and the ^-listen. With <paramref name="lockEvaluations"/> set, the search also carries a
	/// LOCK restriction every candidate passes, whose evaluations are counted there. Returns the matched
	/// keys and the attribute service it counted on.</summary>
	private static async Task<(int[] Keys, IAttributeService Attributes)> Search(
		StrongBox<int>? lockEvaluations, params SearchSpecEngine.SearchPair[] extra)
	{
		var executor = Thing(1, Wizard);
		var things = Enumerable.Range(100, Candidates).Select(key => Thing(key)).ToArray();

		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => things.Select(thing => thing.Object()).ToAsyncEnumerable());
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => new AnyOptionalSharpObject(
				things.Single(thing => thing.Object().Key == call.Arg<GetObjectNodeQuery>().DBRef.Number).Expect<SharpThing>()));

		var attributes = Substitute.For<IAttributeService>();
		attributes.GetVisibleAttributesAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<int>())
			.Returns(call => new ValueTask<SharpAttributesOrError>(call.ArgAt<AnySharpObject>(1).Object().Key % 2 == 1
				? new SharpAttributesOrError(new[] { Attribute("CMD", "$hello:think x"), Attribute("HEAR", "^hi:think y") })
				: new SharpAttributesOrError(new[] { Attribute("OTHER", "nothing") })));

		var locks = Substitute.For<IBooleanExpressionParser>();
		locks.Compile(Arg.Any<string>()).Returns((_, _) =>
		{
			lockEvaluations!.Value++;
			return ValueTask.FromResult(true);
		});

		SearchSpecEngine.SearchPair[] lockPair = lockEvaluations is null ? [] : [new("LOCK", "FLAG^WIZARD|!FLAG^WIZARD")];

		var result = await SearchSpecEngine.ExecuteResultAsync(
			Substitute.For<IMUSHCodeParser>(),
			mediator,
			Substitute.For<ILocateService>(),
			attributes,
			locks,
			Substitute.For<IPermissionService>(),
			executor,
			ownerFilter: null,
			[new SearchSpecEngine.SearchPair("COMMAND", "hello"), new SearchSpecEngine.SearchPair("LISTEN", "hi"), .. lockPair, .. extra],
			useRegex: false);

		return ([.. result.Matches.Select(o => o.Key)], attributes);
	}

	private static Task<(int[] Keys, IAttributeService Attributes)> Search(params SearchSpecEngine.SearchPair[] extra)
		=> Search(null, extra);

	[Test]
	public async Task CommandAndListenReadTheVisibleAttributesOncePerCandidate()
	{
		var (keys, attributes) = await Search();

		await Assert.That(keys).IsEquivalentTo(new[] { 101, 103, 105, 107, 109 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await attributes.Received(Candidates).GetVisibleAttributesAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<int>());
	}

	/// <summary>A lock is softcode that may have side effects, so every candidate is evaluated whatever
	/// the page, as PennMUSH's raw_search does; the page is the same one the full list gave.</summary>
	[Test]
	[Arguments("1", "2", new[] { 103, 105 })]
	[Arguments("0", "1", new[] { 101 })]
	[Arguments("4", "10", new[] { 109 })]
	[Arguments("5", "1", new int[0])]
	[Arguments("2", "0", new int[0])]
	[Arguments("-3", "2", new[] { 101, 103 })]
	[Arguments("2", "-1", new int[0])]
	public async Task WithALockEveryCandidateIsEvaluatedAndStartAndCountSelectThePage(string start, string count, int[] expected)
	{
		var lockEvaluations = new StrongBox<int>();
		var (keys, attributes) = await Search(lockEvaluations,
			new SearchSpecEngine.SearchPair("START", start), new SearchSpecEngine.SearchPair("COUNT", count));

		await Assert.That(keys).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lockEvaluations.Value).IsEqualTo(Candidates);
		await attributes.Received(Candidates).GetVisibleAttributesAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<int>());
	}

	/// <summary>With only read-only restrictions, the same page comes back, and no candidate after the
	/// page's last match is read.</summary>
	[Test]
	[Arguments("1", "2", new[] { 103, 105 }, 6)]
	[Arguments("0", "1", new[] { 101 }, 2)]
	[Arguments("4", "10", new[] { 109 }, Candidates)]
	[Arguments("5", "1", new int[0], Candidates)]
	[Arguments("2", "0", new int[0], 0)]
	[Arguments("-3", "2", new[] { 101, 103 }, 4)]
	[Arguments("2", "-1", new int[0], 0)]
	public async Task WithoutSideEffectsTheScanStopsOnceThePageIsFull(string start, string count, int[] expected, int reads)
	{
		var (keys, attributes) = await Search(new SearchSpecEngine.SearchPair("START", start), new SearchSpecEngine.SearchPair("COUNT", count));

		await Assert.That(keys).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await attributes.Received(reads).GetVisibleAttributesAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<int>());
	}

	[Test]
	public async Task StartAloneSkipsAndCountAloneTakes()
	{
		await Assert.That((await Search(new SearchSpecEngine.SearchPair("START", "3"))).Keys)
			.IsEquivalentTo(new[] { 107, 109 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await Search(new SearchSpecEngine.SearchPair("COUNT", "2"))).Keys)
			.IsEquivalentTo(new[] { 101, 103 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A small page holds only its own matches however many arrive, and reserves no more room
	/// than it can use.</summary>
	[Test]
	[Arguments(0, 3)]
	[Arguments(500, 3)]
	[Arguments(99_998, 3)]
	public async Task TheWindowStoresOnlyItsPage(int start, int count)
	{
		var window = new SearchSpecEngine.ResultWindow(start, count);
		var match = Thing(100).Object();

		for (var offered = 0; offered < 100_000; offered++)
		{
			window.Offer(match);
		}

		await Assert.That(window.Results.Count).IsEqualTo(Math.Min(count, 100_000 - start));
		await Assert.That(window.Results.Capacity).IsLessThanOrEqualTo(count);
	}
}
