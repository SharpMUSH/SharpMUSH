using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Penn's <c>atr_get_with_parent</c> / <c>can_read_attr_internal</c> (pennmush src/attrib.c:325,
/// 1232-1252) walk every backtick-delimited segment of a branch path when resolving an attribute
/// through a parent object, and return NULL / deny outright the moment any segment carries
/// AF_PRIVATE (<c>no_inherit</c>) — this test is guarded by <c>target != obj</c>, so it only fires
/// while crossing a parent/ancestor boundary, never against an object's own attributes.
///
/// SharpMUSH's <c>GetAttributeWithInheritanceAsync</c> (all three providers) previously tested
/// only the leaf attribute's own flags, so a <c>no_inherit</c> flag on an intermediate branch
/// leaked every leaf beneath it to a child through <c>@parent</c>. <see cref="AttributeTreeParentPermissionTests.Parent_NoInheritOnBranch_BlocksChildren"/>
/// pinned that leak as expected behavior; it has been corrected alongside this fix.
/// </summary>
public class AttributeTreeInheritTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	private async Task Cmd(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));
	}

	/// <summary>
	/// Parent has BRANCH (no_inherit) and BRANCH`LEAF. A child @parented to it must not see
	/// BRANCH`LEAF via get(), even though the leaf attribute itself carries no flags at all —
	/// the block comes from the ancestor's branch, not the leaf.
	/// </summary>
	[Test]
	public async ValueTask NoInheritBranch_BlocksLeafThroughObjectParent_Get()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NIGParent");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NIGChild");

		await Cmd($"&BRANCH{uid} {parent}=branchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {parent}=leafvalue");
		await Cmd($"@set {parent}/BRANCH{uid}=no_inherit");
		await Cmd($"@parent {child}={parent}");

		// Control: an unflagged sibling branch is inherited normally, so a miss on the
		// no_inherit branch below is the flag itself, not a broken lookup or @parent wiring.
		await Cmd($"&OK{uid} {parent}=okbranch");
		await Cmd($"&OK{uid}`LEAF {parent}=okleaf");
		var control = await Eval(1, $"get({child}/OK{uid}`LEAF)");
		await Assert.That(control).IsEqualTo("okleaf")
			.Because("an unflagged branch's leaf must still be inherited through @parent");

		var result = await Eval(1, $"get({child}/BRANCH{uid}`LEAF)");
		await Assert.That(result).IsEqualTo(string.Empty)
			.Because("no_inherit on an ancestor branch must block every leaf beneath it, per Penn's AF_Private test in atr_get_with_parent");
	}

	/// <summary>
	/// Same scenario, but through lattrp() — the leaf must not even be listed, not merely
	/// return an empty value.
	/// </summary>
	[Test]
	public async ValueTask NoInheritBranch_BlocksLeafThroughObjectParent_Lattrp()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NILParent");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NILChild");

		await Cmd($"&BRANCH{uid} {parent}=branchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {parent}=leafvalue");
		await Cmd($"@set {parent}/BRANCH{uid}=no_inherit");
		await Cmd($"@parent {child}={parent}");

		// Control: an unflagged sibling branch's leaf is listed by lattrp() through @parent.
		await Cmd($"&OK{uid} {parent}=okbranch");
		await Cmd($"&OK{uid}`LEAF {parent}=okleaf");
		var control = await Eval(1, $"lattrp({child}/OK{uid}`LEAF)");
		await Assert.That(control).Contains($"OK{uid}`LEAF")
			.Because("an unflagged branch's leaf must still be listed by lattrp() through @parent");

		var result = await Eval(1, $"lattrp({child}/BRANCH{uid}`LEAF)");
		await Assert.That(result).DoesNotContain($"BRANCH{uid}`LEAF")
			.Because("no_inherit on an ancestor branch must hide every leaf beneath it from lattrp() too");
	}

	/// <summary>
	/// The no_inherit test only applies while crossing a parent/ancestor boundary
	/// (Penn: guarded by <c>target != obj</c>). An object must still read its own attributes
	/// under its own no_inherit-flagged branch — the flag is about propagation to children,
	/// not self-access.
	/// </summary>
	[Test]
	public async ValueTask NoInheritBranch_DoesNotBlockOwnObjectsOwnLeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NISelf");

		await Cmd($"&BRANCH{uid} {obj}=branchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {obj}=leafvalue");
		await Cmd($"@set {obj}/BRANCH{uid}=no_inherit");

		var result = await Eval(1, $"get({obj}/BRANCH{uid}`LEAF)");
		await Assert.That(result).IsEqualTo("leafvalue")
			.Because("no_inherit only blocks propagation to children — an object must still read its own attribute tree");
	}

	/// <summary>
	/// Penn's atr_get_with_parent (attrib.c:1232-1252) tests each branch-prefix segment for
	/// AF_Private on the ancestor currently being examined BEFORE checking whether the full
	/// leaf resolves there, and returns NULL outright on a hit — it never falls through to a
	/// more distant ancestor for the same attribute path, even if that ancestor's own copy of
	/// the leaf is unflagged.
	///
	/// Grandparent has BRANCH and BRANCH`LEAF, both unflagged. The intermediate parent has only
	/// BRANCH (no LEAF of its own), set no_inherit. A naive "does the full path resolve here"
	/// gate (checking only whole-length matches per ancestor) skips the parent as a non-match
	/// and lets the grandparent's leaf leak straight through — this is the exact leak the task
	/// closes, displaced by one ancestor level.
	/// </summary>
	[Test]
	public async ValueTask NoInheritBranch_OnIntermediateParent_BlocksGrandparentsLeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var grandparent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NIGGrand");
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NIGMid");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NIGLeafChild");

		await Cmd($"&BRANCH{uid} {grandparent}=grandbranchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {grandparent}=leafvalue");
		// Parent has ONLY the branch attribute (no LEAF of its own), flagged no_inherit.
		await Cmd($"&BRANCH{uid} {parent}=parentbranchvalue");
		await Cmd($"@set {parent}/BRANCH{uid}=no_inherit");
		await Cmd($"@parent {parent}={grandparent}");
		await Cmd($"@parent {child}={parent}");

		// Control: an unflagged branch two levels up, with the intermediate parent having no
		// attribute of that name at all, is still inherited straight through — so a miss below
		// is the no_inherit branch specifically, not a broken multi-level @parent chain.
		await Cmd($"&OK{uid} {grandparent}=okbranch");
		await Cmd($"&OK{uid}`LEAF {grandparent}=okleaf");
		var control = await Eval(1, $"get({child}/OK{uid}`LEAF)");
		await Assert.That(control).IsEqualTo("okleaf")
			.Because("an unflagged branch two levels up must still be inherited through an intermediate parent with no attribute of that name");

		var result = await Eval(1, $"get({child}/BRANCH{uid}`LEAF)");
		await Assert.That(result).IsEqualTo(string.Empty)
			.Because("no_inherit on the intermediate parent's branch must block resolution outright, even though that parent has no LEAF of its own and the grandparent's copy is unflagged");
	}

	/// <summary>
	/// Penn's atr_iter_get_parent (attrib.c:1580-1622) -- the wildcard/pattern ancestor walk
	/// that backs lattrp() -- inserts an attribute's name into its "seen" set BEFORE testing
	/// AF_Private:
	/// <code>if (!st_insert(AL_NAME(ptr), &amp;seen) &amp;&amp; ...) continue; // dup
	/// if (parent != thing) { if (AF_Private(ptr)) continue; }</code>
	/// So a private copy on a nearer ancestor still claims the name, shadowing a farther
	/// ancestor's unflagged copy of the same attribute -- even though the nearer, private copy
	/// is itself never returned. GetAttributeQueryHandler's GetAttributesWithParentsAsync must
	/// mark a name as seen before the no_inherit filter runs, not after, or the farther
	/// ancestor's copy leaks through once the nearer one is skipped.
	/// </summary>
	[Test]
	public async ValueTask NoInheritLeaf_OnNearerParent_ShadowsFartherAncestorsUnflaggedCopy()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var grandparent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NISGrand");
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NISMid");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NISLeafChild");

		// Grandparent's copy is a plain, unflagged BRANCH`LEAF.
		await Cmd($"&BRANCH{uid} {grandparent}=grandbranchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {grandparent}=grandleafvalue");

		// The intermediate parent has its OWN copy of the same leaf, flagged no_inherit.
		await Cmd($"&BRANCH{uid} {parent}=parentbranchvalue");
		await Cmd($"&BRANCH{uid}`LEAF {parent}=parentleafvalue");
		await Cmd($"@set {parent}/BRANCH{uid}`LEAF=no_inherit");
		await Cmd($"@parent {parent}={grandparent}");
		await Cmd($"@parent {child}={parent}");

		// Control: an unflagged same-named leaf exists on both parent and grandparent and is
		// still listed once (from the nearer ancestor) -- confirms the multi-level @parent
		// chain and dedup mechanics work at all, so the miss below is the shadowing rule
		// specifically.
		await Cmd($"&OK{uid}`LEAF {grandparent}=grandokvalue");
		await Cmd($"&OK{uid}`LEAF {parent}=parentokvalue");
		var control = await Eval(1, $"lattrp({child}/OK{uid}`LEAF)");
		await Assert.That(control).Contains($"OK{uid}`LEAF")
			.Because("an unflagged same-named leaf on a nearer ancestor is still listed through @parent");

		var result = await Eval(1, $"lattrp({child}/BRANCH{uid}`LEAF)");
		await Assert.That(result).DoesNotContain($"BRANCH{uid}`LEAF")
			.Because("no_inherit on the nearer parent's leaf must shadow the farther grandparent's unflagged copy of the same name, per Penn's seen-before-private-test ordering in atr_iter_get_parent");
	}
}
