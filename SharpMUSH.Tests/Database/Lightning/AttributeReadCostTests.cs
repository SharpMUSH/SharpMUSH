using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// What the attribute reads cost, counted rather than inferred: each test owns its LMDB world, so the
/// provider's <see cref="AttributeReadStats"/> counts only that test's reads. Pins the bounded paths of
/// #1466 (name listings read no values), #1467 (an inherited read stops at the first decisive candidate),
/// #1469 (pattern scans are time-bounded) and #1471 (direct-child and top-level reads skip descendants),
/// and the orderings and boundaries those paths must keep.
/// </summary>
public class AttributeReadCostTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<DBRef> Thing(string name)
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsContainer;
		return await _db.CreateThingAsync(name, room, await God(), room);
	}

	private async Task<AnySharpObject> Node(DBRef dbref) => (await _db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task Parent(DBRef child, DBRef parent)
		=> await _db.SetObjectParent(await Node(child), await Node(parent));

	private async Task Set(DBRef target, string[] path, string value)
		=> await _db.SetAttributeAsync(target, path, MarkupText.Plain(value), await God());

	private async Task SetMany(DBRef target, IEnumerable<string[]> paths)
	{
		var owner = await God();
		await _db.SetAttributesAsync(target, [.. paths.Select(path => new AttributeWrite(path, MarkupText.Plain("v"), owner, []))]);
	}

	private async Task<string[]> ChildNames(DBRef target, string[] branch)
	{
		var node = await _db.GetLazyAttributeAsync(target, branch).LastAsync();
		var leaves = await node.Leaves.WithCancellation(CancellationToken.None);
		return await leaves.Select(x => x.LongName).ToArrayAsync();
	}

	private async Task<string[]> TopLevelNames(DBRef target)
		=> await (await Node(target)).Object().LazyAttributes.Value.Select(x => x.LongName).ToArrayAsync();

	private async Task<string[]> AllNames(DBRef target)
		=> await (await Node(target)).Object().LazyAllAttributes.Value.Select(x => x.LongName).ToArrayAsync();

	private long MetaRows => _db.ReadStats.MetaRowReads;

	// ---- #1471: direct children and the top level skip descendant ranges ----

	[Test]
	public async Task DirectChildrenDoNotReadGrandchildren()
	{
		var target = await Thing("Tree");
		await SetMany(target, [["FOO", "A"], ["FOO", "B"], ["FOO", "C"]]);

		var before = MetaRows;
		var shallow = await ChildNames(target, ["FOO"]);
		var shallowCost = MetaRows - before;

		await SetMany(target, Enumerable.Range(0, 500).Select(i => new[] { "FOO", "A", $"G{i:D3}" }));
		await SetMany(target, Enumerable.Range(0, 500).Select(i => new[] { "FOO", "B", $"G{i:D3}", "DEEP" }));

		before = MetaRows;
		var deep = await ChildNames(target, ["FOO"]);
		var deepCost = MetaRows - before;

		await Assert.That(deep).IsEquivalentTo(shallow, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(deep).IsEquivalentTo(new[] { "FOO`A", "FOO`B", "FOO`C" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		// One extra row per child that has descendants (the first one, which triggers the seek past
		// them) — not one per descendant.
		await Assert.That(deepCost).IsLessThanOrEqualTo(shallowCost + 2);
	}

	[Test]
	public async Task TopLevelDoesNotReadDescendants()
	{
		var target = await Thing("Tree");
		await SetMany(target, [["ALPHA"], ["BETA"], ["GAMMA"]]);

		var before = MetaRows;
		var shallow = await TopLevelNames(target);
		var shallowCost = MetaRows - before;

		await SetMany(target, Enumerable.Range(0, 1000).Select(i => new[] { "BETA", $"G{i:D4}" }));

		before = MetaRows;
		var deep = await TopLevelNames(target);
		var deepCost = MetaRows - before;

		await Assert.That(deep).IsEquivalentTo(shallow, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(deepCost).IsLessThanOrEqualTo(shallowCost + 1);
	}

	/// <summary>
	/// The seek past a child's subtree must not skip a sibling that shares the child's name as a prefix:
	/// <c>FOO_X</c> and <c>FOO1</c> sort between <c>FOO</c> and <c>FOO`…</c>, <c>FOOZ</c> after it. The
	/// skip-scan agrees with the plain filter (every key, minus those with a further backtick).
	/// </summary>
	[Test]
	public async Task SkipScanKeepsKeyOrderAndBacktickBoundaries()
	{
		var target = await Thing("Tree");
		await SetMany(target, [
			["FOO", "A", "DEEP"], ["FOO", "B"], ["FOO_X"], ["FOO1", "C"], ["FOOZ"], ["FO"], ["FOO", "A", "DEEP", "ER"],
			["FOO", "AB"], ["FOO", "A_"], ["FOO", "A", "Z"], ["ZED"], ["{BRACE}", "IN"]
		]);

		var all = await AllNames(target);
		var expectedTop = all.Where(name => !name.Contains('`')).ToArray();
		var expectedFoo = all.Where(name => name.StartsWith("FOO`") && !name["FOO`".Length..].Contains('`')).ToArray();

		await Assert.That(await TopLevelNames(target)).IsEquivalentTo(expectedTop, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await ChildNames(target, ["FOO"])).IsEquivalentTo(expectedFoo, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(expectedFoo).IsEquivalentTo(new[] { "FOO`A", "FOO`AB", "FOO`A_", "FOO`B" });
	}

	[Test]
	public async Task WideBranchesArePagedAndEagerMatchesLazy()
	{
		var target = await Thing("Tree");
		var count = LightningDatabase.ChildPageSize * 2 + 17;
		await SetMany(target, Enumerable.Range(0, count).SelectMany(i => new[]
		{
			new[] { "WIDE", $"C{i:D4}" },
			new[] { "WIDE", $"C{i:D4}", "UNDER" }
		}));

		var lazy = await ChildNames(target, ["WIDE"]);
		var node = await _db.GetAttributeAsync(target, ["WIDE"]).LastAsync();
		var eager = await (await node.Leaves.WithCancellation(CancellationToken.None)).ToArrayAsync();

		await Assert.That(lazy.Length).IsEqualTo(count);
		await Assert.That(lazy).IsInOrder();
		await Assert.That(eager.Select(x => x.LongName)).IsEquivalentTo(lazy, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(eager.All(x => x.Value.ToPlainText() == "v")).IsTrue();
	}

	/// <summary>
	/// Pattern scans and whole-tree reads hydrate each page inside the transaction that read it: a set wider than
	/// any page still comes back whole and in key order, eager and lazy alike, and a scan abandoned after its
	/// first match has looked at the first, small page only.
	/// </summary>
	[Test]
	public async Task PatternScansArePagedInKeyOrderAndStopWhenAbandoned()
	{
		var target = await Thing("Tree");
		var count = 300 + 7;
		await SetMany(target, Enumerable.Range(0, count).Select(i => new[] { $"SCAN{i:D4}" }));
		var expected = Enumerable.Range(0, count).Select(i => $"SCAN{i:D4}").ToArray();

		var eager = await _db.GetAttributesAsync(target, "SCAN*").ToArrayAsync();
		var lazy = await _db.GetLazyAttributesByRegexAsync(target, "^SCAN").Select(x => x.LongName).ToArrayAsync();
		var all = await (await Node(target)).Object().AllAttributes.Value.Select(x => x.LongName).ToArrayAsync();

		await Assert.That(eager.Select(x => x.LongName)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(eager.All(x => x.Value.ToPlainText() == "v")).IsTrue();
		await Assert.That(lazy).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(all).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await AllNames(target)).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var before = MetaRows;
		var first = await _db.GetLazyAttributesAsync(target, "SCAN*").FirstAsync();
		await Assert.That(first.LongName).IsEqualTo("SCAN0000");
		await Assert.That(MetaRows - before).IsLessThanOrEqualTo(LightningStore.FirstMapPageSize);
	}

	[Test]
	public async Task LazyChildrenReadNoValuesAndStopWhenAbandoned()
	{
		var target = await Thing("Tree");
		await SetMany(target, Enumerable.Range(0, 50).Select(i => new[] { "FOO", $"C{i:D2}" }));

		var values = _db.ReadStats.ValueReads;
		var node = await _db.GetLazyAttributeAsync(target, ["FOO"]).LastAsync();
		var first = await (await node.Leaves.WithCancellation(CancellationToken.None)).Take(1).ToArrayAsync();
		await Assert.That(first.Select(x => x.LongName)).IsEquivalentTo(new[] { "FOO`C00" });
		await Assert.That(_db.ReadStats.ValueReads).IsEqualTo(values);

		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		var leaves = await node.Leaves.WithCancellation(CancellationToken.None);
		await Assert.That(async () => await leaves.ToArrayAsync(cancelled.Token)).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task ClearKeepsDescendantsAndWipeRemovesThem()
	{
		var target = await Thing("Tree");
		await SetMany(target, [["FOO", "A", "X"], ["FOO", "B"]]);

		await _db.ClearAttributeAsync(target, ["FOO", "A"]);
		await Assert.That(await ChildNames(target, ["FOO"])).IsEquivalentTo(new[] { "FOO`A", "FOO`B" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(await ChildNames(target, ["FOO", "A"])).IsEquivalentTo(new[] { "FOO`A`X" });

		await _db.WipeAttributeAsync(target, ["FOO", "A"]);
		await Assert.That(await ChildNames(target, ["FOO"])).IsEquivalentTo(new[] { "FOO`B" });

		await _db.WipeAttributeAsync(target, ["FOO", "B"]);
		var foo = await _db.GetLazyAttributeAsync(target, ["FOO"]).LastAsync();
		await Assert.That(foo.Flags.Select(f => f.Name)).DoesNotContain("branch");
		await Assert.That(await ChildNames(target, ["FOO"])).IsEmpty();
	}

	// ---- #1466: name listings read metadata only ----

	[Test]
	public async Task LazyPatternScansAndPathReadsReadNoValues()
	{
		var target = await Thing("Tree");
		await SetMany(target, [["FOO", "A"], ["FOO", "B", "C"], ["BAR"]]);

		var values = _db.ReadStats.ValueReads;
		var glob = await _db.GetLazyAttributesAsync(target, "**").Select(x => x.LongName).ToArrayAsync();
		var regex = await _db.GetLazyAttributesByRegexAsync(target, "^FOO").Select(x => x.LongName).ToArrayAsync();
		var path = await _db.GetLazyAttributeAsync(target, ["FOO", "B", "C"]).ToArrayAsync();

		await Assert.That(glob).IsEquivalentTo(new[] { "BAR", "FOO", "FOO`A", "FOO`B", "FOO`B`C" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(regex).IsEquivalentTo(new[] { "FOO", "FOO`A", "FOO`B", "FOO`B`C" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(path.Length).IsEqualTo(3);
		await Assert.That(_db.ReadStats.ValueReads).IsEqualTo(values);
	}

	[Test]
	public async Task ReadValueOnceReleasesTheBodyAndRereadsOnDemand()
	{
		var target = await Thing("Tree");
		await Set(target, ["BODY"], "first");
		var attr = await _db.GetLazyAttributeAsync(target, ["BODY"]).LastAsync();

		var values = _db.ReadStats.ValueReads;
		await Assert.That((await attr.ReadValueOnceAsync(CancellationToken.None)).ToPlainText()).IsEqualTo("first");
		await Assert.That(attr.Value.IsValueCreated).IsFalse();
		await Set(target, ["BODY"], "second");
		await Assert.That((await attr.ReadValueOnceAsync(CancellationToken.None)).ToPlainText()).IsEqualTo("second");
		await Assert.That(_db.ReadStats.ValueReads - values).IsEqualTo(2);
	}

	// ---- #1467: an inherited read stops at the first decisive candidate ----

	[Test]
	public async Task NearestParentHitReadsNoFartherParentOrAncestor()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var grandparent = await Thing("Grandparent");
		var ancestor = await Thing("Ancestor");
		await Parent(child, parent);
		await Parent(parent, grandparent);
		foreach (var holder in new[] { parent, grandparent, ancestor })
		{
			await Set(holder, ["ROOT", "BRANCH", "LEAF"], $"from #{holder.Number}");
		}

		var walk = new InheritanceWalk(ancestor, InheritanceWalk.DefaultMaxParents);
		var walks = _db.ReadStats.PathWalks;
		var values = _db.ReadStats.ValueReads;
		var eager = await _db.GetAttributeWithInheritanceAsync(child, ["ROOT", "BRANCH", "LEAF"], walk: walk).SingleAsync();

		await Assert.That(eager.SourceObject.Number).IsEqualTo(parent.Number);
		await Assert.That(eager.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(eager.Attributes[^1].Value.ToPlainText()).IsEqualTo($"from #{parent.Number}");
		// The object itself, then the parent: nothing beyond, and only the winner's three values.
		await Assert.That(_db.ReadStats.PathWalks - walks).IsEqualTo(2);
		await Assert.That(_db.ReadStats.ValueReads - values).IsEqualTo(3);

		values = _db.ReadStats.ValueReads;
		var lazy = await _db.GetLazyAttributeWithInheritanceAsync(child, ["ROOT", "BRANCH", "LEAF"], walk: walk).SingleAsync();
		await Assert.That(lazy.SourceObject.Number).IsEqualTo(eager.SourceObject.Number);
		await Assert.That(lazy.Source).IsEqualTo(eager.Source);
		await Assert.That(lazy.Attributes.Select(x => x.LongName)).IsEquivalentTo(eager.Attributes.Select(x => x.LongName), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lazy.InheritedFlags.Select(x => x.Name)).IsEquivalentTo(eager.InheritedFlags.Select(x => x.Name));
		await Assert.That(_db.ReadStats.ValueReads).IsEqualTo(values);
	}

	[Test]
	public async Task APartialPathIsNotAHitAndTheWalkContinues()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var grandparent = await Thing("Grandparent");
		await Parent(child, parent);
		await Parent(parent, grandparent);
		await Set(child, ["ROOT", "OTHER"], "self partial");
		await Set(parent, ["ROOT", "BRANCH"], "parent partial");
		await Set(grandparent, ["ROOT", "BRANCH", "LEAF"], "grandparent");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["ROOT", "BRANCH", "LEAF"]).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(found.SourceObject.Number).IsEqualTo(grandparent.Number);
		await Assert.That(found.Attributes.Select(x => x.LongName))
			.IsEquivalentTo(new[] { "ROOT", "ROOT`BRANCH", "ROOT`BRANCH`LEAF" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// PennMUSH's <c>atr_get_with_parent</c> returns NULL out of the whole lookup on a no_inherit prefix
	/// (<c>src/attrib.c:1240-1243</c>), so the type ancestor is never read.
	/// </summary>
	[Test]
	public async Task NoInheritOnABranchWithoutALeafStopsBeforeTheAncestor()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ancestor = await Thing("Ancestor");
		await Parent(child, parent);
		await Set(parent, ["ROOT", "BRANCH"], "barrier");
		var noInherit = await _db.GetAttributeFlagAsync("no_inherit");
		await _db.SetAttributeFlagAsync((await Node(parent)).Object(), ["ROOT"], noInherit!);
		await Set(ancestor, ["ROOT", "BRANCH", "LEAF"], "ancestor");

		var walks = _db.ReadStats.PathWalks;
		var found = await _db.GetAttributeWithInheritanceAsync(child, ["ROOT", "BRANCH", "LEAF"],
			walk: new InheritanceWalk(ancestor, InheritanceWalk.DefaultMaxParents)).ToListAsync();

		await Assert.That(found).IsEmpty();
		await Assert.That(_db.ReadStats.PathWalks - walks).IsEqualTo(2);
	}

	/// <summary>
	/// A miss walks the object's chain, then the ancestor's (<c>src/attrib.c:1262-1269</c>); an ancestor
	/// already in the explicit chain is read there and not again (<c>:1226</c>).
	/// </summary>
	[Test]
	public async Task AMissWalksTheChainThenTheAncestorsChainOnce()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ancestor = await Thing("Ancestor");
		var ancestorParent = await Thing("AncestorParent");
		await Parent(child, parent);
		await Parent(ancestor, ancestorParent);
		var walk = new InheritanceWalk(ancestor, InheritanceWalk.DefaultMaxParents);

		var walks = _db.ReadStats.PathWalks;
		var found = await _db.GetAttributeWithInheritanceAsync(child, ["MISSING"], walk: walk).ToListAsync();

		await Assert.That(found).IsEmpty();
		// child, parent, ancestor, ancestor's parent — once each.
		await Assert.That(_db.ReadStats.PathWalks - walks).IsEqualTo(4);

		await Set(ancestorParent, ["MISSING"], "found on the ancestor's parent");
		var hit = await _db.GetAttributeWithInheritanceAsync(child, ["MISSING"], walk: walk).SingleAsync();
		await Assert.That(hit.Source).IsEqualTo(AttributeSource.Ancestor);
		await Assert.That(hit.SourceObject.Number).IsEqualTo(ancestorParent.Number);

		await Parent(parent, ancestor);
		walks = _db.ReadStats.PathWalks;
		var missing = await _db.GetAttributeWithInheritanceAsync(child, ["ABSENT"], walk: walk).ToListAsync();
		await Assert.That(missing).IsEmpty();
		// child, parent, ancestor, ancestor's parent: the ancestor was in the chain, so no second leg.
		await Assert.That(_db.ReadStats.PathWalks - walks).IsEqualTo(4);
	}

	// ---- #1469: pattern scans are time-bounded ----

	private const string Pathological = "^(A|AA)+C$";

	[Test]
	public async Task APathologicalAttributeRegexTimesOutAndTheNextScanWorks()
	{
		var target = await Thing("Tree");
		await SetMany(target, [[new string('A', 40)], ["FOO", new string('A', 40)], ["BAR"]]);

		var started = System.Diagnostics.Stopwatch.StartNew();
		await Assert.That(async () => await _db.GetLazyAttributesByRegexAsync(target, Pathological).ToArrayAsync())
			.Throws<RegexMatchTimeoutException>();
		await Assert.That(async () => await _db.GetAttributesByRegexAsync(target, Pathological).ToArrayAsync())
			.Throws<RegexMatchTimeoutException>();
		await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));

		var ordinary = await _db.GetLazyAttributesByRegexAsync(target, "^FOO`").Select(x => x.LongName).ToArrayAsync();
		await Assert.That(ordinary).IsEquivalentTo(new[] { $"FOO`{new string('A', 40)}" });
	}

	[Test]
	public async Task AnInvalidAttributeRegexThrowsAParseError()
	{
		var target = await Thing("Tree");
		await Set(target, ["BAR"], "v");

		await Assert.That(async () => await _db.GetLazyAttributesByRegexAsync(target, "+ABC").ToArrayAsync())
			.Throws<RegexParseException>();
	}

	/// <summary>A scan answers to the evaluation's remaining time, not just to a fresh timeout per row.</summary>
	[Test]
	public async Task AnAttributeScanStopsWhenTheBudgetRunsOut()
	{
		var target = await Thing("Tree");
		await SetMany(target, Enumerable.Range(0, 10).Select(i => new[] { $"A{i}" }));

		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(200));
		using var scope = budget.Enter();
		var rows = _db.GetLazyAttributesByRegexAsync(target, "^A").GetAsyncEnumerator();
		await Assert.That(await rows.MoveNextAsync()).IsTrue();
		await Task.Delay(400);
		await Assert.That(async () => await rows.MoveNextAsync()).Throws<OperationCanceledException>();
		await rows.DisposeAsync();
	}

	/// <summary>
	/// A row reached with less than <see cref="SoftcodeRegex.MatchTimeout"/> left of the budget is matched
	/// within what is left, not within the timeout the filter was built with when the scan began.
	/// </summary>
	[Test]
	public async Task ARowMatchedNearTheDeadlineIsBoundedByWhatIsLeft()
	{
		var target = await Thing("Tree");
		await SetMany(target, [["W"], [new string('X', 40)]]);

		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(400));
		using var scope = budget.Enter();
		var rows = _db.GetLazyAttributesByRegexAsync(target, "^(X|XX)+C$|^W$").GetAsyncEnumerator();
		await Assert.That(await rows.MoveNextAsync()).IsTrue();
		while (budget.Remaining >= TimeSpan.FromMilliseconds(90))
		{
			await Task.Delay(5);
		}

		var timedOut = await Assert.That(async () => await rows.MoveNextAsync()).Throws<RegexMatchTimeoutException>();
		await Assert.That(timedOut!.MatchTimeout).IsLessThan(SoftcodeRegex.MatchTimeout);
		await rows.DisposeAsync();
	}

	[Test]
	public async Task APathologicalWorldNameRegexTimesOut()
	{
		await Thing(new string('A', 40));

		await Assert.That(async () => await _db.GetFilteredObjectsAsync(new ObjectSearchFilter { NamePattern = Pathological, UseRegex = true }).ToArrayAsync())
			.Throws<RegexMatchTimeoutException>();

		var named = await _db.GetFilteredObjectsAsync(new ObjectSearchFilter { NamePattern = "^A{40}$", UseRegex = true }).ToArrayAsync();
		await Assert.That(named.Length).IsEqualTo(1);
	}
}
