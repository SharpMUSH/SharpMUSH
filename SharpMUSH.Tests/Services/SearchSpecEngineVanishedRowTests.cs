using SharpMUSH.Library.Authorization;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The per-object path of <see cref="SearchSpecEngine"/> resolves every row of the filtered scan
/// through <c>GetObjectNodeQuery</c>, so locks and permissions are judged against the canonical
/// cached object and a recycled objid resolves to nothing. A row that no longer resolves — destroyed
/// or recycled between the scan and the lookup — drops out of the result, as it does for
/// <c>@find</c> and as PennMUSH's <c>raw_search</c> skips a garbage object; it does not abort the
/// whole search.
/// </summary>
public class SearchSpecEngineVanishedRowTests
{
	private static AnySharpObject Thing(int key, string name, params SharpObjectFlag[] flags)
	{
		var obj = new SharpObject
		{
			Key = key,
			CreationTime = 1000L + key,
			Name = name,
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
		};

		return new AnySharpObject(new SharpThing
		{
			Object = obj,
			Location = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async _ => { await ValueTask.CompletedTask; return null!; })
		});
	}

	private static readonly SharpObjectFlag Wizard = new()
	{
		Name = "WIZARD",
		Symbol = "W",
		System = true,
		SetPermissions = [],
		UnsetPermissions = [],
		TypeRestrictions = []
	};

	[Test]
	public async Task RowThatNoLongerResolvesIsSkipped()
	{
		// A wizard searcher, so no visibility filtering; the ELOCK class forces the per-object path.
		var executor = Thing(1001, "Searcher", Wizard);
		var vanished = Thing(1002, "Vanished");
		var survivor = Thing(1003, "Survivor");

		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetFilteredObjectsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { vanished.Object(), survivor.Object() }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => call.Arg<GetObjectNodeQuery>().DBRef.Number == survivor.Object().Key
				? new AnyOptionalSharpObject(survivor.Expect<SharpThing>())
				: new AnyOptionalSharpObject(new None()));

		var result = await SearchSpecEngine.ExecuteResultAsync(
			Substitute.For<IMUSHCodeParser>(),
			mediator,
			Substitute.For<ILocateService>(),
			Substitute.For<IAttributeService>(),
			Substitute.For<IBooleanExpressionParser>(),
			Substitute.For<IPermissionService>(),
			executor,
			ownerFilter: null,
			[new SearchSpecEngine.SearchPair("ELOCK", "#TRUE")],
			useRegex: false);

		await Assert.That(result.Expect<SearchSpecEngine.SearchResult>().Matches.Select(o => o.Key)).IsEquivalentTo([survivor.Object().Key]);
	}
}
