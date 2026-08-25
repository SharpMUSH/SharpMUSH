using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Pins <c>@CLONE</c>'s attribute-tree copy against PennMUSH's <c>atr_cpy</c>
/// (<c>src/attrib.c:1692-1710</c>).
/// <para>
/// <c>atr_cpy</c> walks the source's flat, sorted attribute list - branch vs. leaf is purely a
/// naming convention over one namespace - checks <c>AF_Nocopy</c> per attribute, then calls
/// <c>atr_new_add(..., makeroots: false)</c>. With <c>makeroots</c> false, <c>atr_new_add</c>
/// (<c>:756-820</c>) silently aborts without adding when the immediate parent isn't already on
/// the destination (<c>:804-806</c>). Because the source list is sorted parent-before-child, a
/// <c>no_clone</c> BRANCH is itself skipped, and its leaves then find no parent on the clone
/// either and are dropped too - incidentally, via the missing-root abort, not via any permission
/// walk of their own.
/// </para>
/// <para>
/// Every tree assertion here is paired with a positive control: a sibling attribute, at the same
/// depth, that is expected to survive the clone. Before the fix, <c>@CLONE</c> enumerated only
/// <c>GetTopLevelAttributesAsync</c> (depth 1), so every nested attribute - flagged or not - was
/// silently dropped. Without the sibling controls, a "leaf dropped" assertion would pass whether
/// or not <c>no_clone</c> does anything, since depth-1 truncation alone already dropped every
/// leaf.
/// </para>
/// </summary>
public class CloneAttributeTreeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Eval(long handle, string expr)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expr}"));
		return result?.Message?.ToPlainText() ?? "";
	}

	/// <summary>
	/// Runs <paramref name="cmd"/> as <paramref name="handle"/> and returns the plain-text
	/// message it returns (e.g. the new dbref from <c>@create</c> or <c>@clone</c>). Callers that
	/// don't need the result (attribute sets, flag sets) simply discard it.
	/// </summary>
	private async Task<string> Cmd(long handle, string cmd)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single(cmd));
		return result?.Message?.ToPlainText() ?? "";
	}

	/// <summary>
	/// Plain tree, no flags: both the branch and the leaf beneath it are cloned, and the leaf's
	/// full (backtick-joined) name is preserved on the clone.
	/// </summary>
	[Test]
	public async ValueTask PlainTree_BranchAndLeafBothCloned()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("PT");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcPT");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstPT");

		await Cmd(1, $"&{uid} {src}=branchval_{uid}");
		await Cmd(1, $"&{uid}`LEAF {src}=leafval_{uid}");

		// Positive control: the source tree genuinely exists before we ever clone it.
		await Assert.That(await Eval(1, $"hasattr({src},{uid})")).IsEqualTo("1")
			.Because("the source branch must exist for this test to mean anything");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`LEAF)")).IsEqualTo("1")
			.Because("the source leaf must exist for this test to mean anything");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("an unflagged branch attribute must be cloned");
		await Assert.That(await Eval(1, $"get({clone}/{uid})")).IsEqualTo($"branchval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`LEAF)")).IsEqualTo("1")
			.Because("an unflagged leaf beneath an unflagged branch must be cloned - this is red before the fix, since today's @CLONE only enumerates depth 1");
		await Assert.That(await Eval(1, $"get({clone}/{uid}`LEAF)")).IsEqualTo($"leafval_{uid}")
			.Because("the leaf's full backtick-joined LongName must be preserved on the clone");
	}

	/// <summary>
	/// <c>no_clone</c> on the leaf only: the branch is cloned, the leaf is dropped, and an
	/// unflagged sibling leaf under the same branch IS cloned (the positive control that proves
	/// the drop is the flag's doing, not depth-1 truncation).
	/// </summary>
	[Test]
	public async ValueTask NoCloneOnLeafOnly_BranchClonedLeafDropped()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("NL");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcNL");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstNL");

		await Cmd(1, $"&{uid} {src}=branchval_{uid}");
		await Cmd(1, $"&{uid}`BAR {src}=leafval_{uid}");
		await Cmd(1, $"&{uid}`SIB {src}=sibval_{uid}");
		await Cmd(1, $"@set {src}/{uid}`BAR=no_clone");

		// Positive controls: the source tree exists, and the flag actually landed.
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`BAR)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`SIB)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasflag({src}/{uid}`BAR,no_clone)")).IsEqualTo("1")
			.Because("the no_clone flag must actually be set on the source leaf for this test to exercise anything");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("the unflagged branch itself must still be cloned");
		await Assert.That(await Eval(1, $"get({clone}/{uid})")).IsEqualTo($"branchval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`SIB)")).IsEqualTo("1")
			.Because("positive control: an unflagged sibling leaf under the same branch must be cloned - red before the fix (depth-1 truncation drops every leaf), proving the BAR drop below is no_clone's doing and not truncation");
		await Assert.That(await Eval(1, $"get({clone}/{uid}`SIB)")).IsEqualTo($"sibval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`BAR)")).IsEqualTo("0")
			.Because("no_clone on the leaf must prevent it from being cloned");
	}

	/// <summary>
	/// <c>no_clone</c> on the branch, leaf unflagged: BOTH are dropped, because atr_cpy skips the
	/// branch itself, and the leaf then has no parent on the destination
	/// (<c>atr_new_add</c>'s makeroots=false abort). An unrelated, unflagged nested sibling tree
	/// is the positive control proving general tree-copy still works.
	/// </summary>
	[Test]
	public async ValueTask NoCloneOnBranch_UnflaggedLeaf_BothDropped()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("NB");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcNB");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstNB");

		await Cmd(1, $"&{uid} {src}=branchval_{uid}");
		await Cmd(1, $"&{uid}`BAR {src}=leafval_{uid}");
		await Cmd(1, $"@set {src}/{uid}=no_clone");

		// Unrelated, unflagged nested tree: the positive control.
		await Cmd(1, $"&{uid}QUX {src}=quxval_{uid}");
		await Cmd(1, $"&{uid}QUX`SUB {src}=subval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({src},{uid})")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`BAR)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasflag({src}/{uid},no_clone)")).IsEqualTo("1")
			.Because("the no_clone flag must actually be set on the source branch for this test to exercise anything");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}QUX`SUB)")).IsEqualTo("1")
			.Because("positive control: an unrelated, unflagged nested tree must still be cloned - red before the fix (depth-1 truncation drops every leaf), proving the whole clone did not simply fail")
			;
		await Assert.That(await Eval(1, $"get({clone}/{uid}QUX`SUB)")).IsEqualTo($"subval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("0")
			.Because("no_clone on the branch itself must prevent it from being cloned - red before the fix, since today's @CLONE copies every depth-1 attribute regardless of flags");
		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`BAR)")).IsEqualTo("0")
			.Because("the leaf's parent branch was never copied, so the leaf must be dropped too (Penn's makeroots=false abort), even though the leaf itself carries no flag");
	}

	/// <summary>
	/// Three levels, <c>no_clone</c> on the middle node: the root is copied, the middle node and
	/// everything beneath it are dropped. An unflagged sibling under the same root is the
	/// positive control.
	/// </summary>
	[Test]
	public async ValueTask ThreeLevels_NoCloneOnMiddle_RootCopied_RestDropped()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("TL");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcTL");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstTL");

		await Cmd(1, $"&{uid} {src}=aval_{uid}");
		await Cmd(1, $"&{uid}`B {src}=bval_{uid}");
		await Cmd(1, $"&{uid}`B`C {src}=cval_{uid}");
		await Cmd(1, $"&{uid}`D {src}=dval_{uid}");
		await Cmd(1, $"@set {src}/{uid}`B=no_clone");

		await Assert.That(await Eval(1, $"hasattr({src},{uid})")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`B)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`B`C)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`D)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasflag({src}/{uid}`B,no_clone)")).IsEqualTo("1")
			.Because("the no_clone flag must actually be set on B for this test to exercise anything");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("the root A must be cloned");
		await Assert.That(await Eval(1, $"get({clone}/{uid})")).IsEqualTo($"aval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`D)")).IsEqualTo("1")
			.Because("positive control: an unflagged sibling under the same root A must be cloned - red before the fix (depth-1 truncation drops every nested attribute), isolating the drop below to no_clone on B specifically");
		await Assert.That(await Eval(1, $"get({clone}/{uid}`D)")).IsEqualTo($"dval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`B)")).IsEqualTo("0")
			.Because("no_clone on B must prevent it from being cloned");
		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`B`C)")).IsEqualTo("0")
			.Because("C's parent B was never copied, so C must be dropped too even though C itself carries no flag - Penn's makeroots=false abort");
	}

	/// <summary>
	/// A cloned attribute keeps its ORIGINAL creator, not the cloner - PennMUSH's atr_cpy
	/// (<c>attrib.c:1706</c>) passes <c>AL_CREATOR(ptr)</c> through unchanged. Clones as an
	/// executor (a wizard) different from the attribute's original setter (a mortal player who
	/// owns the source object).
	/// </summary>
	[Test]
	public async ValueTask ClonedAttribute_PreservesOriginalCreator_NotTheCloner()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("CR");
		var setter = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CloneCreatorSetter");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstCR");

		// The setter owns the source object and sets the attribute themselves, so the attribute's
		// creator is the setter.
		var src = await Cmd(setter.Handle, $"@create CloneSrcCR_{uid}");
		await Cmd(setter.Handle, $"&{uid} {src}=val_{uid}");

		// Positive control: the source attribute really is owned by the setter, not by whichever
		// player #1 (the wizard about to do the cloning) happens to be.
		await Assert.That(await Eval(1, $"owner({src}/{uid})")).IsEqualTo($"#{setter.DbRef.Number}")
			.Because("sanity check on the test setup: the source attribute's creator must be the setter");
		await Assert.That(await Eval(1, $"hasattr({src},{uid})")).IsEqualTo("1");

		// Player #1 (wizard, handle 1) is a DIFFERENT player from the setter and does the cloning.
		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("the attribute must actually be cloned for the owner check below to mean anything");
		await Assert.That(await Eval(1, $"get({clone}/{uid})")).IsEqualTo($"val_{uid}");

		await Assert.That(await Eval(1, $"owner({clone}/{uid})")).IsEqualTo($"#{setter.DbRef.Number}")
			.Because("the clone's attribute must keep the ORIGINAL creator (the setter), not the cloner (#1) - red before the fix, since SetAttributeAsync stamps the executor as owner");
	}

	/// <summary>
	/// A cloned attribute keeps its ORIGINAL flags, not just its value/creator - PennMUSH's
	/// <c>atr_cpy</c> assigns <c>AL_FLAGS(ptr)</c> alongside <c>AL_CREATOR(ptr)</c> on the very
	/// same <c>atr_new_add</c> call (<c>attrib.c:1706-1707</c>). Preserving only the creator and
	/// dropping the flags would silently strip <c>visual</c>, <c>no_inherit</c>, <c>wizard</c>,
	/// and <c>safe</c> off every cloned attribute.
	/// </summary>
	[Test]
	public async ValueTask ClonedAttribute_PreservesFlags()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("FL");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcFL");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstFL");

		await Cmd(1, $"&{uid} {src}=val_{uid}");
		await Cmd(1, $"@set {src}/{uid}=visual no_inherit");

		// Positive controls: the flags actually landed on the source.
		await Assert.That(await Eval(1, $"hasflag({src}/{uid},visual)")).IsEqualTo("1")
			.Because("the visual flag must actually be set on the source for this test to exercise anything");
		await Assert.That(await Eval(1, $"hasflag({src}/{uid},no_inherit)")).IsEqualTo("1")
			.Because("the no_inherit flag must actually be set on the source for this test to exercise anything");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("the attribute must actually be cloned for the flag checks below to mean anything");
		await Assert.That(await Eval(1, $"get({clone}/{uid})")).IsEqualTo($"val_{uid}");

		await Assert.That(await Eval(1, $"hasflag({clone}/{uid},visual)")).IsEqualTo("1")
			.Because("atr_cpy assigns AL_FLAGS alongside AL_CREATOR on the same call (attrib.c:1706-1707) - red before this fix round, since only the creator was preserved and the flags were silently dropped");
		await Assert.That(await Eval(1, $"hasflag({clone}/{uid},no_inherit)")).IsEqualTo("1")
			.Because("same as above, for the second flag - proves the whole source flag set survives, not just one flag by coincidence");
	}

	/// <summary>
	/// The pre-existing "_"-prefix skip (orthogonal to Penn's no_clone) must propagate to a
	/// non-"_" child the same way no_clone's skip does: otherwise SetAttributeAsync's
	/// auto-vivification (<c>ArangoDatabase.Attributes.cs:608-675</c>) would silently recreate a
	/// stripped, empty "_"-branch on the clone just to hang the child off of.
	/// </summary>
	[Test]
	public async ValueTask UnderscoreBranch_DropsNonUnderscoreChildToo()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("UB");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcUB");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstUB");

		await Cmd(1, $"&_{uid} {src}=branchval_{uid}");
		await Cmd(1, $"&_{uid}`CHILD {src}=childval_{uid}");
		// Unrelated sibling attribute: the positive control.
		await Cmd(1, $"&{uid}SIB {src}=sibval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({src},_{uid})")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},_{uid}`CHILD)")).IsEqualTo("1");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}SIB)")).IsEqualTo("1")
			.Because("positive control: an unrelated, unflagged attribute must still be cloned - red before the tree fix (depth-1 truncation), proving the clone as a whole worked");
		await Assert.That(await Eval(1, $"get({clone}/{uid}SIB)")).IsEqualTo($"sibval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},_{uid})")).IsEqualTo("0")
			.Because("the pre-existing '_'-prefix filter must still skip a '_'-prefixed branch");
		await Assert.That(await Eval(1, $"hasattr({clone},_{uid}`CHILD)")).IsEqualTo("0")
			.Because("the '_'-prefix skip must propagate to a non-'_' child - otherwise the child would auto-vivify a stripped, empty '_'-branch on the clone just to hang off of, the exact hazard the no_clone propagation exists to prevent");
	}

	/// <summary>
	/// The unstated second half of the "_" handling: because @CLONE now walks the whole tree
	/// (previously only depth-1 attributes were even reachable), a "_"-prefixed LEAF segment
	/// under an otherwise-unflagged branch is newly reachable and newly skipped too - a semantic
	/// choice nobody had pinned before this test. Cannot regress anything that worked before
	/// (this case was unreachable at depth 1), but the choice itself deserves a test.
	/// </summary>
	[Test]
	public async ValueTask UnderscoreLeaf_UnderUnflaggedBranch_IsAlsoDropped()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("UL");
		var src = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CloneSrcUL");
		var cloneName = TestIsolationHelpers.GenerateUniqueName("CloneDstUL");

		await Cmd(1, $"&{uid} {src}=branchval_{uid}");
		await Cmd(1, $"&{uid}`_BAR {src}=underscoreleafval_{uid}");
		await Cmd(1, $"&{uid}`SIB {src}=sibval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({src},{uid}`_BAR)")).IsEqualTo("1");
		await Assert.That(await Eval(1, $"hasattr({src},{uid}`SIB)")).IsEqualTo("1");

		var clone = await Cmd(1, $"@clone {src}={cloneName}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid})")).IsEqualTo("1")
			.Because("the unflagged branch itself must still be cloned");
		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`SIB)")).IsEqualTo("1")
			.Because("positive control: an unflagged sibling leaf under the same branch must be cloned - red before the tree fix (depth-1 truncation)");
		await Assert.That(await Eval(1, $"get({clone}/{uid}`SIB)")).IsEqualTo($"sibval_{uid}");

		await Assert.That(await Eval(1, $"hasattr({clone},{uid}`_BAR)")).IsEqualTo("0")
			.Because("a '_'-prefixed LEAF segment must be skipped just like a '_'-prefixed top-level attribute - previously unreachable at depth 1, so this is a newly-pinned semantic choice rather than a regression risk");
	}
}
