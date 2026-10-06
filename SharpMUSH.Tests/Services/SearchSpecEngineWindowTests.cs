using SharpMUSH.Library.Authorization;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The per-object path of <see cref="SearchSpecEngine"/> (#1468): a search with both COMMAND and LISTEN
/// reads each candidate's $-command set once; a search carrying softcode (a lock or an EVAL)
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
				Grants = new(_ => Task.FromResult(ObjectGrants.None)),
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
	private static Task<(int[] Keys, int Reads)> Search(
		StrongBox<int>? lockEvaluations, params SearchSpecEngine.SearchPair[] extra)
		=> Search(lockEvaluations, null, extra);

	/// <summary>As above; with <paramref name="examinations"/> set, the searcher is a mortal, so every
	/// candidate goes through the visibility check, which passes and is counted there.</summary>
	private static async Task<(int[] Keys, int Reads)> Search(
		StrongBox<int>? lockEvaluations, StrongBox<int>? examinations, params SearchSpecEngine.SearchPair[] extra)
	{
		var executor = examinations is null ? Thing(1, Wizard) : Thing(2);
		var things = Enumerable.Range(100, Candidates).Select(key => Thing(key)).ToArray();

		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => things.Select(thing => thing.Object()).ToAsyncEnumerable());
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => new AnyOptionalSharpObject(
				things.Single(thing => thing.Object().Key == call.Arg<GetObjectNodeQuery>().DBRef.Number).Expect<SharpThing>()));

		// COMMAND is read first, through the candidate's $-command set; only a candidate it admits has its
		// ^-patterns matched.
		var reads = 0;
		mediator.Send(Arg.Any<GetCommandAttributesQuery>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				reads++;
				return new ValueTask<CommandAttributeCache[]>(call.Arg<GetCommandAttributesQuery>().SharpObject.Object().Key % 2 == 1
					? [new CommandAttributeCache(Attribute("CMD", "$hello:think x"), SoftcodeRegex.Wildcard("hello"), false)]
					: []);
			});
		var attributes = Substitute.For<IAttributeService>();
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "LISTEN", Arg.Any<IAttributeService.AttributeMode>(), false)
			.Returns(new ValueTask<OptionalSharpAttributeOrError>(new OptionalSharpAttributeOrError(new None())));
		var listens = Substitute.For<IListenPatternMatcher>();
		listens.MatchListenPatternsAsync(Arg.Any<AnySharpObject>(), "hi", Arg.Any<AnySharpObject>(), Arg.Any<bool>())
			.Returns(call => new ValueTask<ListenMatch[]>(call.ArgAt<AnySharpObject>(0).Object().Key % 2 == 1
				? [new ListenMatch(Attribute("HEAR", "^hi:think y"), [], ListenBehavior.AHear)]
				: []));
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.GetService(typeof(IListenPatternMatcher)).Returns(listens);

		var locks = Substitute.For<IBooleanExpressionParser>();
		locks.Compile(Arg.Any<string>()).Returns((_, _) =>
		{
			lockEvaluations!.Value++;
			return ValueTask.FromResult(true);
		});

		var permissions = Substitute.For<IPermissionService>();
		permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(_ =>
		{
			examinations!.Value++;
			return ValueTask.FromResult(true);
		});

		SearchSpecEngine.SearchPair[] lockPair = lockEvaluations is null ? [] : [new("LOCK", "FLAG^WIZARD|!FLAG^WIZARD")];

		var result = await SearchSpecEngine.ExecuteResultAsync(
			parser,
			mediator,
			Substitute.For<ILocateService>(),
			attributes,
			locks,
			permissions,
			executor,
			ownerFilter: null,
			[new SearchSpecEngine.SearchPair("COMMAND", "hello"), new SearchSpecEngine.SearchPair("LISTEN", "hi"), .. lockPair, .. extra],
			useRegex: false);

		return ([.. result.Expect<SearchSpecEngine.SearchResult>().Matches.Select(o => o.Key)], reads);
	}

	private static Task<(int[] Keys, int Reads)> Search(params SearchSpecEngine.SearchPair[] extra)
		=> Search(null, extra);

	[Test]
	public async Task CommandIsReadOncePerCandidate()
	{
		var (keys, reads) = await Search();

		await Assert.That(keys).IsEquivalentTo(new[] { 101, 103, 105, 107, 109 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(reads).IsEqualTo(Candidates);
	}

	/// <summary>A lock is softcode that may have side effects, so every candidate is evaluated whatever
	/// the page, as PennMUSH's raw_search does; the page is the same one the full list gave.</summary>
	[Test]
	[Arguments("2", "2", new[] { 103, 105 })]
	[Arguments("1", "1", new[] { 101 })]
	[Arguments("5", "10", new[] { 109 })]
	[Arguments("6", "1", new int[0])]
	[Arguments("3", "1", new[] { 105 })]
	public async Task WithALockEveryCandidateIsEvaluatedAndStartAndCountSelectThePage(string start, string count, int[] expected)
	{
		var lockEvaluations = new StrongBox<int>();
		var (keys, reads) = await Search(lockEvaluations,
			new SearchSpecEngine.SearchPair("START", start), new SearchSpecEngine.SearchPair("COUNT", count));

		await Assert.That(keys).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lockEvaluations.Value).IsEqualTo(Candidates);
		await Assert.That(reads).IsEqualTo(Candidates);
	}

	/// <summary>A mortal's search checks each candidate with Can_Examine, which can evaluate Control,
	/// Zone and Examine locks, so it too goes through every candidate, as PennMUSH's raw_search does
	/// (src/wiz.c:2542).</summary>
	[Test]
	[Arguments("1", "1", new[] { 101 })]
	[Arguments("2", "2", new[] { 103, 105 })]
	public async Task AMortalsVisibilityCheckRunsOnEveryCandidate(string start, string count, int[] expected)
	{
		var examinations = new StrongBox<int>();
		var (keys, _) = await Search(null, examinations,
			new SearchSpecEngine.SearchPair("START", start), new SearchSpecEngine.SearchPair("COUNT", count));

		await Assert.That(keys).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(examinations.Value).IsEqualTo(Candidates);
	}

	/// <summary>With only read-only restrictions, the same page comes back, and no candidate after the
	/// page's last match is read.</summary>
	[Test]
	[Arguments("2", "2", new[] { 103, 105 }, 6)]
	[Arguments("1", "1", new[] { 101 }, 2)]
	[Arguments("5", "10", new[] { 109 }, Candidates)]
	[Arguments("6", "1", new int[0], Candidates)]
	[Arguments("1", "2", new[] { 101, 103 }, 4)]
	public async Task WithoutSideEffectsTheScanStopsOnceThePageIsFull(string start, string count, int[] expected, int reads)
	{
		var (keys, actualReads) = await Search(new SearchSpecEngine.SearchPair("START", start), new SearchSpecEngine.SearchPair("COUNT", count));

		await Assert.That(keys).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(actualReads).IsEqualTo(reads);
	}

	[Test]
	public async Task StartAloneSkipsAndCountAloneTakes()
	{
		await Assert.That((await Search(new SearchSpecEngine.SearchPair("START", "4"))).Keys)
			.IsEquivalentTo(new[] { 107, 109 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That((await Search(new SearchSpecEngine.SearchPair("COUNT", "2"))).Keys)
			.IsEquivalentTo(new[] { 101, 103 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>A small page holds only its own matches however many arrive, and reserves no more room
	/// than it can use.</summary>
	[Test]
	[Arguments(1, 3)]
	[Arguments(501, 3)]
	[Arguments(99_999, 3)]
	public async Task TheWindowStoresOnlyItsPage(int start, int count)
	{
		var window = new SearchSpecEngine.ResultWindow(start, count);
		var match = Thing(100).Object();

		for (var offered = 0; offered < 100_000; offered++)
		{
			window.Offer(match);
		}

		await Assert.That(window.Results.Count).IsEqualTo(Math.Min(count, 100_001 - start));
		await Assert.That(window.Results.Capacity).IsLessThanOrEqualTo(count);
	}

	/// <summary>PennMUSH's fill_search_spec (src/wiz.c:2388-2399): START and COUNT are 1-based, and one
	/// below 1 — including text, which parse_integer reads as 0 — rejects the whole spec before anything
	/// is searched.</summary>
	[Test]
	[Arguments("START", "0", nameof(ErrorMessages.Notifications.SearchInvalidStart))]
	[Arguments("START", "-3", nameof(ErrorMessages.Notifications.SearchInvalidStart))]
	[Arguments("START", "first", nameof(ErrorMessages.Notifications.SearchInvalidStart))]
	[Arguments("COUNT", "0", nameof(ErrorMessages.Notifications.SearchInvalidCount))]
	[Arguments("COUNT", "-1", nameof(ErrorMessages.Notifications.SearchInvalidCount))]
	public async Task AStartOrCountBelowOneRejectsTheSpec(string type, string value, string notification)
	{
		var mediator = Substitute.For<IMediator>();
		var result = await SearchSpecEngine.ExecuteResultAsync(
			Substitute.For<IMUSHCodeParser>(), mediator, Substitute.For<ILocateService>(), Substitute.For<IAttributeService>(),
			Substitute.For<IBooleanExpressionParser>(), Substitute.For<IPermissionService>(), Thing(1, Wizard),
			ownerFilter: null, [new SearchSpecEngine.SearchPair(type, value)], useRegex: false);

		await Assert.That(result.Expect<Error<string>>().Value).IsEqualTo(notification);
		mediator.DidNotReceiveWithAnyArgs().CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments("7", 7)]
	[Arguments("  12abc", 12)]
	[Arguments("+3", 3)]
	[Arguments("-4", -4)]
	[Arguments("abc", 0)]
	[Arguments("", 0)]
	[Arguments("99999999999", int.MaxValue)]
	public async Task RestrictionsReadAsPennMUSHParseIntegerReadsThem(string text, int expected)
		=> await Assert.That(SearchSpecEngine.LeadingInteger(text)).IsEqualTo(expected);
}
