using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>GetAttributesQuery</c> and <c>GetLazyAttributesQuery</c> are one documented contract
/// (<c>GetLazyAttributesQuery</c> is literally an <c>&lt;inheritdoc&gt;</c> of the eager one) with a
/// <c>CheckParents</c> flag, so a caller that switches to the lazy read to avoid materialising a
/// large result must get the same attributes back. The lazy handler used to ignore
/// <c>CheckParents</c> entirely and return only the object's own attributes.
/// </summary>
/// <remarks>
/// The fixture covers everything the eager walk does beyond "also read the parent": shadowing (the
/// child's own copy wins and the parent's is not yielded a second time), the leaf's own
/// <c>no_inherit</c>, and <c>no_inherit</c> on a BRANCH, which blocks the whole subtree and is only
/// caught by re-resolving the matched attribute's full path on the parent.
/// </remarks>
public class LazyAttributeParentParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private async ValueTask<AnySharpObject> CreateAsync(string name)
	{
		var result = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {name}"));
		var dbref = DBRef.Parse(result.Message!.ToPlainText()!);
		return (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
	}

	private async ValueTask<(AnySharpObject Child, AnySharpObject Parent)> BuildFixtureAsync(string label)
	{
		var parent = await CreateAsync($"{label}Parent");
		var child = await CreateAsync($"{label}Child");
		await Mediator.Send(new SetObjectParentCommand(child, parent));

		// Inherited, plain.
		await AttributeService.SetAttributeAsync(parent, parent, $"{label}_INHERITED", MarkupText.Plain("from parent"));

		// Shadowed: both hold it; the child's copy is the one that must surface, once.
		await AttributeService.SetAttributeAsync(parent, parent, $"{label}_SHADOWED", MarkupText.Plain("parent copy"));
		await AttributeService.SetAttributeAsync(child, child, $"{label}_SHADOWED", MarkupText.Plain("child copy"));

		// The child's own.
		await AttributeService.SetAttributeAsync(child, child, $"{label}_OWN", MarkupText.Plain("child only"));

		// no_inherit on the leaf itself.
		await AttributeService.SetAttributeAsync(parent, parent, $"{label}_SECRET", MarkupText.Plain("not inherited"));
		await AttributeService.SetAttributeFlagAsync(parent, parent, $"{label}_SECRET", "no_inherit");

		// no_inherit on a BRANCH: the leaf carries no flag of its own, and only a full-path
		// re-resolution on the parent can tell that the whole subtree is blocked.
		await AttributeService.SetAttributeAsync(parent, parent, $"{label}_TREE`LEAF", MarkupText.Plain("blocked leaf"));
		await AttributeService.SetAttributeFlagAsync(parent, parent, $"{label}_TREE", "no_inherit");

		// An open tree, to prove the branch case is about the flag and not about trees.
		await AttributeService.SetAttributeAsync(parent, parent, $"{label}_OPEN`LEAF", MarkupText.Plain("open leaf"));

		return (child, parent);
	}

	private async ValueTask<string[]> EagerAsync(
		AnySharpObject child, string pattern, IAttributeService.AttributePatternMode mode, bool checkParents = true)
	{
		var result = await AttributeService.GetAttributePatternAsync(child, child, pattern, checkParents, mode);
		await Assert.That(result.IsError).IsFalse();
		return result.Expect<SharpAttribute[]>().Select(a => a.LongName!).Order(StringComparer.Ordinal).ToArray();
	}

	private async ValueTask<string[]> LazyAsync(
		AnySharpObject child, string pattern, IAttributeService.AttributePatternMode mode, bool checkParents = true)
	{
		var result = await AttributeService.LazilyGetAttributePatternAsync(child, child, pattern, checkParents, mode);
		await Assert.That(result.IsError).IsFalse();
		var names = await result.Expect<IAsyncEnumerable<LazySharpAttribute>>().Select(a => a.LongName).ToArrayAsync();
		return names.Order(StringComparer.Ordinal).ToArray();
	}

	/// <summary>
	/// A glob <c>*</c> does not cross a <c>`</c>, so the wildcard read sees only the top level:
	/// the inherited leaf, the child's own, the one copy of the shadowed name, and the open
	/// branch node. The <c>no_inherit</c> leaf and the <c>no_inherit</c> branch are both blocked.
	/// </summary>
	[Test]
	[Timeout(30_000)]
	public async ValueTask LazyWildcardReadWithCheckParents_MatchesTheEagerRead(CancellationToken ct)
	{
		const string label = "PARITY";
		var (child, _) = await BuildFixtureAsync(label);

		string[] expected =
		[
			$"{label}_INHERITED",
			$"{label}_OPEN",
			$"{label}_OWN",
			$"{label}_SHADOWED"
		];

		var eager = await EagerAsync(child, $"{label}_*", IAttributeService.AttributePatternMode.Wildcard);
		await Assert.That(eager).IsEquivalentTo(expected)
			.Because("the eager walk is the reference behaviour this fixture is calibrated against");

		var lazy = await LazyAsync(child, $"{label}_*", IAttributeService.AttributePatternMode.Wildcard);
		await Assert.That(lazy).IsEquivalentTo(expected)
			.Because("GetLazyAttributesQuery documents the same contract as GetAttributesQuery");
	}

	/// <summary>
	/// The regex read reaches into the trees, which is what makes the branch <c>no_inherit</c> case
	/// observable: <c>_OPEN`LEAF</c> comes through and <c>_TREE`LEAF</c> does not, even though
	/// neither leaf carries a flag of its own.
	/// </summary>
	[Test]
	[Timeout(30_000)]
	public async ValueTask LazyRegexReadWithCheckParents_MatchesTheEagerReadThroughTrees(CancellationToken ct)
	{
		const string label = "PARITYRX";
		var (child, _) = await BuildFixtureAsync(label);

		string[] expected =
		[
			$"{label}_INHERITED",
			$"{label}_OPEN",
			$"{label}_OPEN`LEAF",
			$"{label}_OWN",
			$"{label}_SHADOWED"
		];

		var eager = await EagerAsync(child, $"^{label}_", IAttributeService.AttributePatternMode.Regex);
		await Assert.That(eager).IsEquivalentTo(expected)
			.Because("the eager walk is the reference behaviour this fixture is calibrated against");

		var lazy = await LazyAsync(child, $"^{label}_", IAttributeService.AttributePatternMode.Regex);
		await Assert.That(lazy).IsEquivalentTo(expected)
			.Because("a no_inherit branch must block its whole subtree on the lazy path too");
	}

	[Test]
	[Timeout(30_000)]
	public async ValueTask LazyPatternReadWithoutCheckParents_StaysOnTheObject(CancellationToken ct)
	{
		const string label = "PARITYSELF";
		var (child, _) = await BuildFixtureAsync(label);

		string[] expected = [$"{label}_OWN", $"{label}_SHADOWED"];

		await Assert.That(await EagerAsync(child, $"{label}_*", IAttributeService.AttributePatternMode.Wildcard, false))
			.IsEquivalentTo(expected);
		await Assert.That(await LazyAsync(child, $"{label}_*", IAttributeService.AttributePatternMode.Wildcard, false))
			.IsEquivalentTo(expected);
	}

	[Test]
	[Timeout(30_000)]
	public async ValueTask LazyPatternReadWithCheckParents_TerminatesOnAParentCycle(CancellationToken ct)
	{
		var a = await CreateAsync("LazyCycleA");
		var b = await CreateAsync("LazyCycleB");

		// Bypasses SafeToAddParent on both edges, the way AttributeReadParentCycleTests does.
		await Mediator.Send(new SetObjectParentCommand(a, b));
		await Mediator.Send(new SetObjectParentCommand(b, a));
		await AttributeService.SetAttributeAsync(a, a, "LAZYCYCLE_HERE", MarkupText.Plain("hello"));

		var result = await AttributeService.LazilyGetAttributePatternAsync(
			a, a, "LAZYCYCLE_*", checkParents: true, IAttributeService.AttributePatternMode.Wildcard);

		await Assert.That(result.IsError).IsFalse();
		var names = await result.Expect<IAsyncEnumerable<LazySharpAttribute>>().Select(x => x.LongName).ToArrayAsync();
		await Assert.That(names).Contains("LAZYCYCLE_HERE");
	}
}
