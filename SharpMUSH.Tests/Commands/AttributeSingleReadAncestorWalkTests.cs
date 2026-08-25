using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The SINGLE-attribute read path - <c>get()</c>, <c>xget()</c>, <c>ufun</c>, <c>@examine obj/attr</c>,
/// and everything else routed through <c>GetAttributeAsync</c>/<c>LazilyGetAttributeAsync</c> - used
/// to flag-test only the root..leaf path <b>as resolved on the source object</b>, and never walked
/// targets outward from the object the lookup was made against.
/// <para>
/// PennMUSH's <c>can_read_attr_internal</c> (<c>src/attrib.c:318-356</c>) re-walks the
/// <c>`</c>-separated ancestor path on every access over TARGETS: <c>target = obj</c> first, then
/// outward along the <c>@parent</c> chain. Two consequences the source-only test misses, both
/// fail-OPEN:
/// </para>
/// <list type="bullet">
/// <item>
/// A prefix that EXISTS on a nearer target and fails the flag test is <c>return 0</c> inline
/// (<c>attrib.c:331-335</c>) - it does not <c>goto continue_target</c>. So a restrictively-flagged
/// branch of the same name on the child denies a leaf inherited from a permissive parent.
/// </item>
/// <item>
/// The same applies to an INTERMEDIATE object in a longer chain. The provider treats a parent that
/// holds only part of the path as "incomplete" and quietly walks on to the grandparent
/// (<c>ArangoDatabase.Attributes.cs</c>, <c>EvaluateInheritanceCandidateAsync</c>); only
/// <c>no_inherit</c> aborts. Penn instead flag-tests that partial prefix and denies on it.
/// </item>
/// </list>
/// <para>
/// Every test here pairs the denial with a positive control of the SAME shape whose only difference
/// is the flag under test, so a miss can never be read as "the @parent chain never resolved".
/// </para>
/// </summary>
public class AttributeSingleReadAncestorWalkTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	/// <summary>PennMUSH's ANCESTOR_THING, which the seeded database points at #6.</summary>
	private static readonly DBRef AncestorThing = new(6);

	private async Task Cmd(long handle, string command)
		=> await Parser.CommandParse(handle, ConnectionService, MModule.single(command));

	/// <summary>
	/// Evaluates <paramref name="expression"/> AS the player behind <paramref name="handle"/>.
	/// <c>FunctionParse</c> always runs as the parser's bound executor (God), who takes every
	/// privileged early-out, so a viewer-sensitive read has to go through <c>think</c>.
	/// </summary>
	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	private async Task<AnySharpObject> Known(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

	private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpper();

	/// <summary>
	/// Scenario b. The child shadows the parent's branch name with a <c>mortal_dark</c> copy of its
	/// own and holds NO leaf; the parent holds a fully visual branch AND the leaf. Penn denies at
	/// the child (<c>attrib.c:331</c>). The old code resolved the whole path on the PARENT - where
	/// every level is visual - and handed back the value.
	/// </summary>
	[Test]
	public async ValueTask ShadowingMortalDarkBranchOnChild_DeniesGetOfLeafInheritedFromParent()
	{
		var uid = Uid();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkBP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkBC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkBV");

		// Both trees are identical on the parent: visual branch, visual leaf, distinct values.
		await Cmd(1, $"&BO{uid} {parent}=openbranch");
		await Cmd(1, $"&BO{uid}`PUB {parent}=openleaf{uid}");
		await Cmd(1, $"@set {parent}/BO{uid}=visual");
		await Cmd(1, $"@set {parent}/BO{uid}`PUB=visual");

		await Cmd(1, $"&BD{uid} {parent}=quietbranch");
		await Cmd(1, $"&BD{uid}`PUB {parent}=darkleaf{uid}");
		await Cmd(1, $"@set {parent}/BD{uid}=visual");
		await Cmd(1, $"@set {parent}/BD{uid}`PUB=visual");

		await Cmd(1, $"@parent {child.DbRef}={parent}");

		// The child shadows BOTH branch names and holds NEITHER leaf. The control's copy is visual;
		// the subject's is mortal_dark. Nothing else differs between the two trees.
		await Cmd(1, $"&BO{uid} {child.DbRef}=childopen");
		await Cmd(1, $"@set {child.DbRef}/BO{uid}=visual");
		await Cmd(1, $"&BD{uid} {child.DbRef}=childquiet");
		await Cmd(1, $"@set {child.DbRef}/BD{uid}=mortal_dark");

		var control = await Eval(viewer.Handle, $"get({child.DbRef}/BO{uid}`PUB)");
		await Assert.That(control).IsEqualTo($"openleaf{uid}")
			.Because("the @parent chain must still deliver the leaf across a VISUAL shadowing branch");

		var result = await Eval(viewer.Handle, $"get({child.DbRef}/BD{uid}`PUB)");
		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.AttrPermissions)
			.Because("a prefix present on the child that fails the flag test is attrib.c:331's inline return 0 - "
				+ "and the read must DENY, not merely come back empty for some unrelated reason");
	}

	/// <summary>
	/// Scenario e. Chain child -&gt; parent -&gt; grandparent. The parent holds ONLY the branch, and
	/// it is <c>mortal_dark</c>; the grandparent holds a visual branch and the leaf. The provider
	/// calls the parent "incomplete" and walks past it to the grandparent, so the old code never saw
	/// the parent's flag at all. Penn stops at the parent and returns 0.
	/// </summary>
	[Test]
	public async ValueTask MortalDarkPartialBranchOnIntermediateParent_DeniesGetOfLeafFromGrandparent()
	{
		var uid = Uid();
		var grandparent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkEG");
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkEP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkEC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkEV");

		// Grandparent holds both complete trees, fully visual.
		await Cmd(1, $"&EO{uid} {grandparent}=openbranch");
		await Cmd(1, $"&EO{uid}`PUB {grandparent}=openleaf{uid}");
		await Cmd(1, $"@set {grandparent}/EO{uid}=visual");
		await Cmd(1, $"@set {grandparent}/EO{uid}`PUB=visual");

		await Cmd(1, $"&ED{uid} {grandparent}=quietbranch");
		await Cmd(1, $"&ED{uid}`PUB {grandparent}=darkleaf{uid}");
		await Cmd(1, $"@set {grandparent}/ED{uid}=visual");
		await Cmd(1, $"@set {grandparent}/ED{uid}`PUB=visual");

		// The intermediate parent holds ONLY the branch of each tree - never the leaf. Control's is
		// visual, the subject's is mortal_dark.
		await Cmd(1, $"&EO{uid} {parent}=midopen");
		await Cmd(1, $"@set {parent}/EO{uid}=visual");
		await Cmd(1, $"&ED{uid} {parent}=midquiet");
		await Cmd(1, $"@set {parent}/ED{uid}=mortal_dark");

		await Cmd(1, $"@parent {parent}={grandparent}");
		await Cmd(1, $"@parent {child.DbRef}={parent}");

		var control = await Eval(viewer.Handle, $"get({child.DbRef}/EO{uid}`PUB)");
		await Assert.That(control).IsEqualTo($"openleaf{uid}")
			.Because("a two-hop @parent chain past a visual partial branch must still deliver the grandparent's leaf");

		var result = await Eval(viewer.Handle, $"get({child.DbRef}/ED{uid}`PUB)");
		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.AttrPermissions)
			.Because("Penn flag-tests the partial prefix on the intermediate target and denies there - only no_inherit "
				+ "skips a target - and the read must DENY, not merely come back empty");
	}

	/// <summary>
	/// Scenario c: the child's own branch is visual, the parent's is <c>mortal_dark</c> and the
	/// parent holds the leaf. Already denied before this change - the source-resolved path happened
	/// to contain the offending node - and pinned here so the two halves of the walk (the nearer
	/// targets and the source itself) cannot drift apart.
	/// </summary>
	[Test]
	public async ValueTask MortalDarkBranchOnTheParentThatHoldsTheLeaf_DeniesGet()
	{
		var uid = Uid();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkCP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkCC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkCV");

		await Cmd(1, $"&CO{uid} {parent}=openbranch");
		await Cmd(1, $"&CO{uid}`PUB {parent}=openleaf{uid}");
		await Cmd(1, $"@set {parent}/CO{uid}=visual");
		await Cmd(1, $"@set {parent}/CO{uid}`PUB=visual");

		await Cmd(1, $"&CD{uid} {parent}=quietbranch");
		await Cmd(1, $"&CD{uid}`PUB {parent}=darkleaf{uid}");
		await Cmd(1, $"@set {parent}/CD{uid}=mortal_dark");
		await Cmd(1, $"@set {parent}/CD{uid}`PUB=visual");

		await Cmd(1, $"@parent {child.DbRef}={parent}");

		// The child holds a visual copy of BOTH branch names, so the walk crosses the child cleanly
		// in either case and the only variable left is the parent's own flag.
		await Cmd(1, $"&CO{uid} {child.DbRef}=childopen");
		await Cmd(1, $"@set {child.DbRef}/CO{uid}=visual");
		await Cmd(1, $"&CD{uid} {child.DbRef}=childquiet");
		await Cmd(1, $"@set {child.DbRef}/CD{uid}=visual");

		var control = await Eval(viewer.Handle, $"get({child.DbRef}/CO{uid}`PUB)");
		await Assert.That(control).IsEqualTo($"openleaf{uid}")
			.Because("a visual branch on both objects must still deliver the parent's leaf");

		var result = await Eval(viewer.Handle, $"get({child.DbRef}/CD{uid}`PUB)");
		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.AttrPermissions)
			.Because("the parent's mortal_dark branch governs the parent's own leaf - and the read must DENY, "
				+ "not merely come back empty");
	}

	/// <summary>
	/// The lazy overload is the same read and must gate identically. Scenario b's shape, driven
	/// straight at <see cref="IAttributeService.LazilyGetAttributeAsync"/> so the lazy code path is
	/// exercised rather than inferred from the eager one.
	/// </summary>
	[Test]
	public async ValueTask LazilyGetAttribute_ShadowingMortalDarkBranchOnChild_Denies()
	{
		var uid = Uid();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkLP");
		var childRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkLC");
		var viewerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "WalkLV");

		await Cmd(1, $"&LO{uid} {parent}=openbranch");
		await Cmd(1, $"&LO{uid}`PUB {parent}=openleaf{uid}");
		await Cmd(1, $"@set {parent}/LO{uid}=visual");
		await Cmd(1, $"@set {parent}/LO{uid}`PUB=visual");

		await Cmd(1, $"&LD{uid} {parent}=quietbranch");
		await Cmd(1, $"&LD{uid}`PUB {parent}=darkleaf{uid}");
		await Cmd(1, $"@set {parent}/LD{uid}=visual");
		await Cmd(1, $"@set {parent}/LD{uid}`PUB=visual");

		await Cmd(1, $"@parent {childRef}={parent}");

		await Cmd(1, $"&LO{uid} {childRef}=childopen");
		await Cmd(1, $"@set {childRef}/LO{uid}=visual");
		await Cmd(1, $"&LD{uid} {childRef}=childquiet");
		await Cmd(1, $"@set {childRef}/LD{uid}=mortal_dark");

		var child = await Known(childRef);
		var viewer = await Known(viewerRef);

		var control = await AttributeService.LazilyGetAttributeAsync(viewer, child, $"LO{uid}`PUB",
			IAttributeService.AttributeMode.Read);
		await Assert.That(control.IsAttribute).IsTrue()
			.Because("the lazy path must still resolve an inherited leaf across a visual shadowing branch");

		var result = await AttributeService.LazilyGetAttributeAsync(viewer, child, $"LD{uid}`PUB",
			IAttributeService.AttributeMode.Read);
		await Assert.That(result.IsAttribute).IsFalse()
			.Because("the lazy overload must apply the same target walk as the eager one");
		await Assert.That(result.IsError).IsTrue()
			.Because("a failed flag test is a permission error, not an absent attribute");
	}

	/// <summary>
	/// Fail-CLOSED guard. <c>ParentChainAsync</c> follows <c>@parent</c> only, so a zone-sourced
	/// result's source object is not in the chain: running the walk over it would fall off the end
	/// and deny (<c>attrib.c:356</c>), breaking zone tree attributes that read fine today.
	/// </summary>
	[Test]
	public async ValueTask ZoneSourcedTreeAttribute_StillResolvesForAMortal()
	{
		var uid = Uid();
		var zone = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkZZ");
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkZO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkZV");

		await Cmd(1, $"&ZT{uid} {zone}=zonebranch");
		await Cmd(1, $"&ZT{uid}`PUB {zone}=zoneleaf{uid}");
		await Cmd(1, $"@set {zone}/ZT{uid}=visual");
		await Cmd(1, $"@set {zone}/ZT{uid}`PUB=visual");

		await Cmd(1, $"@chzone {obj}={zone}");

		var result = await Eval(viewer.Handle, $"get({obj}/ZT{uid}`PUB)");
		await Assert.That(result).IsEqualTo($"zoneleaf{uid}")
			.Because("a zone source is not in the @parent chain, so the target walk must be skipped for it");
	}

	/// <summary>
	/// The type-ancestor fall-through (ANCESTOR_THING, #6) with the leaf ON the ancestor itself: the
	/// ordinary shape, and the one that must keep working. It pins the FALL-THROUGH, not any skip -
	/// the walk now runs over ancestor-sourced results too, so this is the fail-CLOSED control for
	/// <see cref="MortalDarkBranchOnTheObject_DeniesLeafFromTheTypeAncestor"/>.
	/// </summary>
	[Test]
	public async ValueTask AncestorSourcedTreeAttribute_StillResolvesForAMortal()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkAO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkAV");

		await Cmd(1, $"&AT{uid} {AncestorThing}=ancestorbranch");
		await Cmd(1, $"&AT{uid}`PUB {AncestorThing}=ancestorleaf{uid}");
		await Cmd(1, $"@set {AncestorThing}/AT{uid}=visual");
		await Cmd(1, $"@set {AncestorThing}/AT{uid}`PUB=visual");

		var result = await Eval(viewer.Handle, $"get({obj}/AT{uid}`PUB)");
		await Assert.That(result).IsEqualTo($"ancestorleaf{uid}")
			.Because("the type ancestor holds a fully visual tree, so a mortal must read the leaf through the fall-through");
	}

	/// <summary>
	/// A flat (backtick-free) name IS its whole path, and Penn returns before the walk ever starts
	/// (<c>attrib.c:311-312</c>). Pinned because short-circuiting it is what keeps the hottest read
	/// path in the server from paying for a parent-chain walk that can decide nothing.
	/// </summary>
	[Test]
	public async ValueTask FlatInheritedAttribute_StillResolvesForAMortal()
	{
		var uid = Uid();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkFP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkFC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkFV");

		await Cmd(1, $"&FT{uid} {parent}=flatleaf{uid}");
		await Cmd(1, $"@set {parent}/FT{uid}=visual");
		await Cmd(1, $"@parent {child.DbRef}={parent}");

		var result = await Eval(viewer.Handle, $"get({child.DbRef}/FT{uid})");
		await Assert.That(result).IsEqualTo($"flatleaf{uid}")
			.Because("a flat name has no ancestors to walk and must read exactly as before");
	}

	/// <summary>
	/// Scenario b transposed one path over: the object holds a <c>mortal_dark</c> branch and no leaf,
	/// and the leaf comes from the TYPE ANCESTOR rather than from an <c>@parent</c>. Penn does not
	/// distinguish the two - <c>target = obj</c> is the first target either way, and its failing
	/// prefix is <c>attrib.c:331</c>'s inline <c>return 0</c> before the ancestor is ever consulted
	/// (Penn only reaches <c>target = ancestor</c> via <c>continue_target</c>, <c>attrib.c:344-353</c>).
	/// The fall-through used to flag-test the ANCESTOR's nodes only, so the object's own branch flag
	/// was never seen.
	/// </summary>
	[Test]
	public async ValueTask MortalDarkBranchOnTheObject_DeniesLeafFromTheTypeAncestor()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkMO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkMV");

		// Two identical fully-visual trees on the type ancestor.
		await Cmd(1, $"&MO{uid} {AncestorThing}=openbranch");
		await Cmd(1, $"&MO{uid}`PUB {AncestorThing}=openleaf{uid}");
		await Cmd(1, $"@set {AncestorThing}/MO{uid}=visual");
		await Cmd(1, $"@set {AncestorThing}/MO{uid}`PUB=visual");

		await Cmd(1, $"&MD{uid} {AncestorThing}=quietbranch");
		await Cmd(1, $"&MD{uid}`PUB {AncestorThing}=darkleaf{uid}");
		await Cmd(1, $"@set {AncestorThing}/MD{uid}=visual");
		await Cmd(1, $"@set {AncestorThing}/MD{uid}`PUB=visual");

		// The object shadows BOTH branch names and holds NEITHER leaf. Control's copy is visual, the
		// subject's is mortal_dark; nothing else differs.
		await Cmd(1, $"&MO{uid} {obj}=objopen");
		await Cmd(1, $"@set {obj}/MO{uid}=visual");
		await Cmd(1, $"&MD{uid} {obj}=objquiet");
		await Cmd(1, $"@set {obj}/MD{uid}=mortal_dark");

		var control = await Eval(viewer.Handle, $"get({obj}/MO{uid}`PUB)");
		await Assert.That(control).IsEqualTo($"openleaf{uid}")
			.Because("the fall-through must still deliver the ancestor's leaf across a VISUAL shadowing branch");

		var result = await Eval(viewer.Handle, $"get({obj}/MD{uid}`PUB)");
		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.AttrPermissions)
			.Because("Penn denies at target = obj before ever reaching the ancestor - the ancestor path is not exempt");
	}

	/// <inheritdoc cref="MortalDarkBranchOnTheObject_DeniesLeafFromTheTypeAncestor"/>
	/// <summary>The lazy overload of the same read must gate identically.</summary>
	[Test]
	public async ValueTask LazilyGetAttribute_MortalDarkBranchOnTheObject_DeniesLeafFromTheTypeAncestor()
	{
		var uid = Uid();
		var objRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkNO");
		var viewerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "WalkNV");

		await Cmd(1, $"&NO{uid} {AncestorThing}=openbranch");
		await Cmd(1, $"&NO{uid}`PUB {AncestorThing}=openleaf{uid}");
		await Cmd(1, $"@set {AncestorThing}/NO{uid}=visual");
		await Cmd(1, $"@set {AncestorThing}/NO{uid}`PUB=visual");

		await Cmd(1, $"&ND{uid} {AncestorThing}=quietbranch");
		await Cmd(1, $"&ND{uid}`PUB {AncestorThing}=darkleaf{uid}");
		await Cmd(1, $"@set {AncestorThing}/ND{uid}=visual");
		await Cmd(1, $"@set {AncestorThing}/ND{uid}`PUB=visual");

		await Cmd(1, $"&NO{uid} {objRef}=objopen");
		await Cmd(1, $"@set {objRef}/NO{uid}=visual");
		await Cmd(1, $"&ND{uid} {objRef}=objquiet");
		await Cmd(1, $"@set {objRef}/ND{uid}=mortal_dark");

		var obj = await Known(objRef);
		var viewer = await Known(viewerRef);

		var control = await AttributeService.LazilyGetAttributeAsync(viewer, obj, $"NO{uid}`PUB",
			IAttributeService.AttributeMode.Read);
		await Assert.That(control.IsAttribute).IsTrue()
			.Because("the lazy fall-through must still resolve the ancestor's leaf across a visual shadowing branch");

		var result = await AttributeService.LazilyGetAttributeAsync(viewer, obj, $"ND{uid}`PUB",
			IAttributeService.AttributeMode.Read);
		await Assert.That(result.IsError).IsTrue()
			.Because("the lazy ancestor fall-through must apply the same target walk as the eager one");
	}

	/// <summary>
	/// The fail-CLOSED counterpart of the two tests above. Penn does not stop at the ancestor: after
	/// <c>target = ancestor</c> it keeps running <c>target = Parent(target)</c>
	/// (<c>attrib.c:344-353</c>), so a leaf on an ANCESTOR-OF-ANCESTOR is still readable. The walk's
	/// target chain therefore has to be <c>obj</c>'s <c>@parent</c> chain followed by the
	/// <b>ancestor's own</b> chain - appending the bare ancestor dbref would leave this result's
	/// source off the end of the chain and deny it (<c>attrib.c:356</c>).
	/// <para>
	/// Mutates <c>@parent</c> on the shared ANCESTOR_THING (#6), so it runs alone and restores the
	/// previous state in a finally.
	/// </para>
	/// </summary>
	[Test]
	[NotInParallel]
	public async ValueTask AncestorOfAncestorSourcedTreeAttribute_StillResolvesForAMortal()
	{
		var uid = Uid();
		var ancestorParent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkPA");
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WalkPO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkPV");

		// The whole tree lives two inheritance hops out: obj -> (type ancestor #6) -> its @parent.
		await Cmd(1, $"&PT{uid} {ancestorParent}=deepbranch");
		await Cmd(1, $"&PT{uid}`PUB {ancestorParent}=deepleaf{uid}");
		await Cmd(1, $"@set {ancestorParent}/PT{uid}=visual");
		await Cmd(1, $"@set {ancestorParent}/PT{uid}`PUB=visual");

		try
		{
			await Cmd(1, $"@parent {AncestorThing}={ancestorParent}");

			var result = await Eval(viewer.Handle, $"get({obj}/PT{uid}`PUB)");
			await Assert.That(result).IsEqualTo($"deepleaf{uid}")
				.Because("the walk's chain must continue through the ancestor's OWN parents, as Penn's does");
		}
		finally
		{
			await Cmd(1, $"@parent {AncestorThing}=none");
		}
	}
}
