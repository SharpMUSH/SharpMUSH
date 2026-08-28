using DotNext.Threading;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

internal static class TestAttributeFactory
{
	/// <summary>
	/// Builds a minimal <see cref="SharpAttribute"/> with the given <see cref="SharpAttribute.LongName"/>
	/// and flags. Suitable for tests that only care about identity/name/flags, not value or lazy relations.
	/// </summary>
	public static SharpAttribute Named(string longName, params string[] flagNames) => new(
		Id: $"attribute/{longName}",
		Key: longName,
		Name: longName,
		Flags: flagNames.Select(f => new SharpAttributeFlag
		{
			Name = f,
			Symbol = f[..1],
			System = false,
			Inheritable = false
		}).ToArray(),
		CommandListIndex: null,
		LongName: longName,
		Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
		Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
		SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(null)));
}

/// <summary>
/// Unit tests for PennMUSH's <c>can_read_attr_internal</c> target walk
/// (<c>src/attrib.c:318-356</c>) as implemented by <see cref="AttributeAncestry"/>. The three
/// outcomes the walk must keep distinct - abandon this target, deny outright, grant - are each
/// pinned separately, since conflating any two of them is a permission bug in one direction or
/// the other.
/// </summary>
public class AttributeAncestryTests
{
	private static readonly DBRef Child = new(10);
	private static readonly DBRef Parent = new(11);
	private static readonly DBRef Grandparent = new(12);

	private static SharpAttribute Attr(string longName, params string[] flags)
		=> TestAttributeFactory.Named(longName, flags);

	/// <summary>
	/// A stand-in database: attributes per object, looked up case-insensitively by long name the
	/// way the production index is. Records every (target, prefix) it was asked for.
	/// </summary>
	private sealed class Store
	{
		private readonly Dictionary<int, Dictionary<string, SharpAttribute>> _byObject = new();

		public List<string> Fetched { get; } = [];

		public Store With(DBRef obj, params SharpAttribute[] attributes)
		{
			var index = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase);
			foreach (var attribute in attributes)
			{
				index[attribute.LongName] = attribute;
			}

			_byObject[obj.Number] = index;
			return this;
		}

		public ValueTask<SharpAttribute?> Fetch(DBRef target, string[] path)
		{
			var name = string.Join('`', path);
			Fetched.Add($"#{target.Number}/{name}");

			return ValueTask.FromResult(
				_byObject.TryGetValue(target.Number, out var index) && index.TryGetValue(name, out var attribute)
					? attribute
					: null);
		}
	}

	/// <summary>
	/// The flag test, standing in for <c>PermissionService.CanViewAttribute</c> for a viewer who
	/// cannot examine the target: every node on the path must be visual (Penn's
	/// <c>AF_Visual</c> grant) and none may be mortal_dark.
	/// </summary>
	private static ValueTask<bool> MortalPermits(SharpAttribute[] path)
		=> ValueTask.FromResult(path.All(a =>
			a.Flags.Any(f => f.Name == "visual") && !a.Flags.Any(f => f.Name == "mortal_dark")));

	[Test]
	public async Task TopLevelAttributeOnTheObjectItself_NeedsNoAncestors()
	{
		var leaf = Attr("FOO", "visual");
		var store = new Store().With(Child, leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue();
		await Assert.That(store.Fetched).IsEmpty()
			.Because("a flat attribute IS its whole path - no prefix lookup should happen at all");
	}

	[Test]
	public async Task VisualBranchAndLeafOnTheObjectItself_IsGranted()
	{
		var branch = Attr("FOO", "visual");
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store().With(Child, branch, leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue();
	}

	[Test]
	public async Task NonVisualBranch_DeniesItsVisualLeaf()
	{
		var branch = Attr("FOO");
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store().With(Child, branch, leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse()
			.Because("Penn requires the flag test to pass on EVERY level, not just the leaf");
	}

	/// <summary>
	/// This previously asserted the opposite - that a missing branch node was simply omitted from
	/// the path, on the reasoning that "no ancestor" is not "a denial". That is wrong against
	/// PennMUSH: <c>attrib.c:324-327</c> abandons the target, and running off the end of the chain
	/// is <c>return 0</c> (<c>attrib.c:356</c>). Omitting the segment was a fail-open, because the
	/// grant condition is "ALL levels pass" - a path collapsed to <c>[leaf]</c> passed on the
	/// leaf's own flag alone.
	/// </summary>
	[Test]
	public async Task OrphanedLeafWithNoFurtherTargets_Denies()
	{
		var leaf = Attr("GONE`LEAF", "visual");
		var store = new Store().With(Child, leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse();
	}

	[Test]
	public async Task InheritedLeaf_ResolvesItsBranchOnTheParentThatHoldsIt()
	{
		// The child has NOTHING - the whole branch lives on the parent, which is the ordinary
		// shape of an inherited tree attribute. Walking only the child would find no prefix.
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child)
			.With(Parent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue();
		await Assert.That(store.Fetched).Contains("#11/FOO")
			.Because("the branch must be looked up on the parent, which is where it exists");
	}

	[Test]
	public async Task MortalDarkBranchOnTheParent_DeniesTheInheritedLeaf()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child)
			.With(Parent, Attr("FOO", "visual", "mortal_dark"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse();
	}

	/// <summary>
	/// The shadowing case. Penn walks outward from <c>target = obj</c> and, when a prefix EXISTS
	/// on the current target but fails the flag test, returns 0 right there
	/// (<c>attrib.c:331-335</c>) - it does NOT <c>goto continue_target</c>. So the child's own
	/// restrictive branch denies even though the parent that actually holds the leaf has a
	/// permissive one. Walking only the source object grants here.
	/// </summary>
	[Test]
	public async Task RestrictiveBranchOnTheChild_DeniesALeafInheritedFromThePermissiveParent()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child, Attr("FOO", "visual", "mortal_dark"))
			.With(Parent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse();
		await Assert.That(store.Fetched).DoesNotContain("#11/FOO")
			.Because("the denial is immediate at the child - the walk must never reach the parent's branch");
	}

	/// <summary>
	/// The counterpart that keeps the fix from over-denying: a MISSING prefix on a nearer target
	/// advances the walk rather than denying it (<c>attrib.c:324-327</c>). Without this, every
	/// parent-inherited tree attribute would break, since the child legitimately lacks the
	/// parent's branch.
	/// </summary>
	[Test]
	public async Task MissingBranchOnTheChild_AdvancesToTheParentRatherThanDenying()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child, Attr("UNRELATED", "visual"))
			.With(Parent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue();
	}

	/// <summary>
	/// A restrictive prefix present on a nearer target must deny even when a LATER prefix is
	/// missing there. Penn tests each prefix as it resolves it, so the flag test on <c>FOO</c>
	/// fires before the lookup of <c>FOO`BAR</c> ever comes back empty - which is why the
	/// prefixes cannot be gathered all-or-nothing before any of them is tested.
	/// </summary>
	[Test]
	public async Task RestrictiveShallowPrefixOnTheChild_DeniesEvenWhenADeeperPrefixIsMissingThere()
	{
		var leaf = Attr("FOO`BAR`BAZ", "visual");
		var store = new Store()
			.With(Child, Attr("FOO", "visual", "mortal_dark"))
			.With(Parent, Attr("FOO", "visual"), Attr("FOO`BAR", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse();
	}

	/// <summary>
	/// <c>attrib.c:325</c>'s <c>(target != obj &amp;&amp; AF_Private(atr))</c>: a no_inherit prefix
	/// on a target OTHER than the original object abandons that target instead of denying, since
	/// no_inherit means "this does not cross an inheritance boundary", not "this is secret".
	/// <para>
	/// The parent's <c>FOO</c> is deliberately NOT visual. Were it visual, ignoring
	/// <c>no_inherit</c> entirely would still pass the flag test, still reach the grandparent and
	/// still return true - the test would be unable to fail.
	/// </para>
	/// </summary>
	[Test]
	public async Task NoInheritBranchOnAnIntermediateTarget_AbandonsThatTargetInsteadOfDenying()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child)
			.With(Parent, Attr("FOO", "no_inherit"))
			.With(Grandparent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Grandparent, [Child, Parent, Grandparent], Child,
			store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue()
			.Because("the no_inherit branch abandons the parent rather than flag-testing it, so the walk reaches the grandparent");
	}

	/// <summary>
	/// <see cref="NoInheritBranchOnAnIntermediateTarget_AbandonsThatTargetInsteadOfDenying"/>, but
	/// the parent's <c>FOO</c> carries the flag stored as <c>NO_INHERIT</c> rather than the
	/// canonical lowercase <c>no_inherit</c>. Every provider's flag catalog is seeded lowercase and
	/// <c>@set</c> always resolves through it, so this casing is unreachable via that path - but
	/// <see cref="AttributeAncestry"/> tests <see cref="SharpAttribute"/> flags handed to it from
	/// wherever a caller sourced them, not just catalog-resolved ones, and CodeRabbit's review of
	/// this branch found three of the four provider-level no_inherit gates doing a case-sensitive
	/// <c>==</c> instead of the case-insensitive test the fourth (SurrealDB) already used - a
	/// production/dev divergence given SurrealDB is what actually runs in production. This test
	/// pins the shared <see cref="SharpAttributeExtensions.IsNoInherit(SharpAttribute)"/> extension
	/// all four gates were pointed at, so a regression in any one of them (or in the extension
	/// itself) shows up here.
	/// <para>
	/// The parent's <c>FOO</c> is deliberately NOT visual, exactly as in the sibling test - were it
	/// visual, a comparison that fails to recognise <c>NO_INHERIT</c> would still pass the flag
	/// test, still reach the grandparent, and still return true, unable to fail.
	/// </para>
	/// </summary>
	[Test]
	public async Task NoInheritBranchOnAnIntermediateTarget_UnexpectedCasingStillAbandons()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child)
			.With(Parent, Attr("FOO", "NO_INHERIT"))
			.With(Grandparent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Grandparent, [Child, Parent, Grandparent], Child,
			store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue()
			.Because("no_inherit stored as NO_INHERIT must still abandon the parent branch, not just the canonical lowercase spelling");
	}

	/// <summary>
	/// The exact counterpart of
	/// <see cref="NoInheritBranchOnAnIntermediateTarget_AbandonsThatTargetInsteadOfDenying"/>, and
	/// the only thing that separates the two outcomes: a prefix present on an INTERMEDIATE target -
	/// one that is neither the origin nor the holder of the leaf - is flag-tested there and denies
	/// on failure (<c>attrib.c:331-335</c>). Only a MISSING prefix, or a <c>no_inherit</c> one, is
	/// allowed to advance the walk.
	/// <para>
	/// This is the shape the single-attribute read path fails open on: the provider treats a parent
	/// holding only part of the path as "incomplete" and walks past it to the grandparent, so its
	/// flag is never consulted. The grandparent's whole tree is deliberately visual - if the
	/// intermediate target were skipped, the walk would reach the grandparent and GRANT, so the
	/// assertion can only pass for the reason it names.
	/// </para>
	/// </summary>
	[Test]
	public async Task RestrictiveBranchOnAnIntermediateTarget_DeniesRatherThanAdvancing()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child)
			.With(Parent, Attr("FOO", "visual", "mortal_dark"))
			.With(Grandparent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Grandparent, [Child, Parent, Grandparent], Child,
			store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse()
			.Because("a failing prefix on a target that holds neither the origin nor the leaf still returns 0 inline");
		await Assert.That(store.Fetched).DoesNotContain("#12/FOO")
			.Because("the denial is immediate at the parent - the walk must never reach the grandparent's visual branch");
	}

	/// <summary>
	/// The same flag on the ORIGINAL object is not an escape - Penn's condition is guarded by
	/// <c>target != obj</c>, so a no_inherit branch on <c>obj</c> itself is flag-tested normally
	/// rather than abandoning the only target there is.
	/// <para>
	/// The branch is visual, so honouring the guard GRANTS. Were it non-visual, the test would
	/// return false whether the guard was honoured (flag test fails) or wrongly ignored (origin
	/// abandoned, chain exhausted) - unable to fail either way.
	/// </para>
	/// </summary>
	[Test]
	public async Task NoInheritBranchOnTheOriginalObject_IsFlagTestedNormally()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store().With(Child, Attr("FOO", "no_inherit", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue()
			.Because("no_inherit must not abandon the origin - its branch is visual, so the read is granted");
	}

	/// <summary>
	/// The child holds a perfectly readable <c>FOO</c>, so every prefix on the only target in the
	/// chain resolves and passes; the sole reason to deny is that the target holding the LEAF was
	/// never reached. Without that visual <c>FOO</c> the walk would abandon the child on a missing
	/// prefix and the test would pass for a reason unrelated to what it claims to pin.
	/// </summary>
	[Test]
	public async Task SourceNotPresentInTheChain_Denies()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child, Attr("FOO", "visual"))
			.With(Parent, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse()
			.Because("running off the end of the chain without reaching the leaf is attrib.c:356's return 0");
	}

	/// <summary>
	/// One object's <c>FOO</c> must never vouch for another object's <c>FOO`BAR</c>. Every prefix
	/// is resolved against the target currently being walked, so the child's readable <c>FOO</c>
	/// cannot stand in for the parent's restrictive one of the same name. Structural in production
	/// (the ancestor cache is keyed by target), and this pins the behaviour it produces.
	/// </summary>
	[Test]
	public async Task APrefixOnOneTarget_DoesNotVouchForTheSameNameOnAnother()
	{
		var leaf = Attr("FOO`BAR", "visual");
		var store = new Store()
			.With(Child, Attr("FOO", "visual"))
			.With(Parent, Attr("FOO", "visual", "mortal_dark"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Parent, [Child, Parent], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse()
			.Because("the parent's own mortal_dark FOO governs the parent's leaf - the child's visual FOO of the same name is a different attribute");
		await Assert.That(store.Fetched).IsEquivalentTo(new[] { "#10/FOO", "#11/FOO" })
			.Because("the same prefix name is resolved separately against each target, never reused across them");
	}

	[Test]
	public async Task DeepPath_ChecksEveryIntermediateLevel()
	{
		var leaf = Attr("A`B`C`D", "visual");
		var store = new Store().With(Child,
			Attr("A", "visual"), Attr("A`B", "visual"), Attr("A`B`C"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsFalse()
			.Because("A`B`C is not visual, and it sits between the root and the leaf");
		// Joined rather than IsEquivalentTo: that assertion ignores order, so it would pass on a
		// leaf-first walk even though root-first is the whole claim being made here.
		await Assert.That(string.Join(" -> ", store.Fetched))
			.IsEqualTo("#10/A -> #10/A`B -> #10/A`B`C")
			.Because("every strict prefix is resolved root-first, and the leaf is never re-fetched");
	}

	[Test]
	public async Task PrefixLookupIsCaseInsensitive()
	{
		var leaf = Attr("Foo`Bar", "visual");
		var store = new Store().With(Child, Attr("FOO", "visual"), leaf);

		var can = await AttributeAncestry.CanReadAsync(leaf, Child, [Child], Child, store.Fetch, MortalPermits);

		await Assert.That(can).IsTrue()
			.Because("attribute names are case-insensitive");
	}
}
