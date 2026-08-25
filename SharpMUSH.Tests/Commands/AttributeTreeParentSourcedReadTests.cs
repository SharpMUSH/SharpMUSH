using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The pattern read path resolves an attribute's ancestors on the object the leaf actually
/// came from. With <c>checkParents</c> the match set contains leaves sourced from a PARENT
/// object, and the ancestor lookup used to query the child instead - finding nothing, dropping
/// the prefix, and collapsing the path to <c>[leaf]</c>, so <c>All(IsVisual)</c> passed
/// trivially and a <c>mortal_dark</c> branch on the parent listed and revealed its leaves.
/// <para>
/// PennMUSH's <c>can_read_attr_internal</c> (<c>src/attrib.c:318-356</c>) walks the
/// backtick-delimited prefix against <c>target</c> - the object currently being examined in the
/// parent chain - not against the original object, and a prefix it cannot find on that target
/// never grants: it <c>goto continue_target</c>s to the next parent and the function ends
/// <c>return 0</c> (<c>attrib.c:356</c>).
/// </para>
/// </summary>
public class AttributeTreeParentSourcedReadTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Runs <paramref name="expression"/> as the player behind <paramref name="handle"/>.
	/// FunctionParse always evaluates as the parser's bound executor (God), which would take the
	/// isPrivileged early-out, so every viewer-sensitive check goes through think.
	/// </summary>
	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	private async Task Cmd(long handle, string command)
		=> await Parser.CommandParse(handle, ConnectionService, MModule.single(command));

	/// <summary>
	/// Parent P holds a <c>mortal_dark</c> branch whose leaf is <c>visual</c>; child C is
	/// <c>@parent P</c> and has no copy of either. A mortal listing the leaf by name on C must
	/// not see it: the branch's flags belong to P, so the ancestor lookup has to happen on P.
	/// </summary>
	[Test]
	public async ValueTask MortalDarkBranchOnParent_HidesInheritedLeafFromLattrp()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SrcDarkP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcDarkC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcDarkV");

		// Control branch: visual at EVERY level, so a mortal legitimately sees the inherited leaf.
		await Cmd(1, $"&SO{uid} {parent}=openbranch");
		await Cmd(1, $"&SO{uid}`PUB {parent}=openleaf");
		await Cmd(1, $"@set {parent}/SO{uid}=visual");
		await Cmd(1, $"@set {parent}/SO{uid}`PUB=visual");

		// The leak: mortal_dark branch, visual leaf, and nothing of either on the child.
		await Cmd(1, $"&SD{uid} {parent}=secretbranch");
		await Cmd(1, $"&SD{uid}`PUB {parent}=secretleaf");
		await Cmd(1, $"@set {parent}/SD{uid}=mortal_dark");
		await Cmd(1, $"@set {parent}/SD{uid}`PUB=visual");

		await Cmd(1, $"@parent {child.DbRef}={parent}");

		// Control: proves the @parent chain resolves and that a leaf-only lattrp pattern against
		// the CHILD really does reach the parent's copy - otherwise the miss below would just mean
		// "inheritance is broken" rather than "correctly denied".
		var control = await Eval(viewer.Handle, $"lattrp({child.DbRef}/SO{uid}`PUB)");
		await Assert.That(control).Contains($"SO{uid}`PUB")
			.Because("a fully-visual inherited leaf must still be listed through the parent chain");

		var result = await Eval(viewer.Handle, $"lattrp({child.DbRef}/SD{uid}`PUB)");
		await Assert.That(result).DoesNotContain($"SD{uid}`PUB")
			.Because("the branch's mortal_dark flag lives on the PARENT, so the ancestor walk must query the parent");
	}

	/// <summary>
	/// Same shape as above with a non-visual branch instead of a <c>mortal_dark</c> one: Penn
	/// requires AF_VISUAL on every level of the path, and the level carrying (or lacking) it is
	/// on the parent object.
	/// </summary>
	[Test]
	public async ValueTask NonVisualBranchOnParent_HidesInheritedVisualLeafFromLattrp()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SrcVisP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcVisC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcVisV");

		await Cmd(1, $"&VO{uid} {parent}=openbranch");
		await Cmd(1, $"&VO{uid}`PUB {parent}=openleaf");
		await Cmd(1, $"@set {parent}/VO{uid}=visual");
		await Cmd(1, $"@set {parent}/VO{uid}`PUB=visual");

		// Leaf is visual; its branch deliberately is not.
		await Cmd(1, $"&VD{uid} {parent}=quietbranch");
		await Cmd(1, $"&VD{uid}`PUB {parent}=quietleaf");
		await Cmd(1, $"@set {parent}/VD{uid}`PUB=visual");

		await Cmd(1, $"@parent {child.DbRef}={parent}");

		var control = await Eval(viewer.Handle, $"lattrp({child.DbRef}/VO{uid}`PUB)");
		await Assert.That(control).Contains($"VO{uid}`PUB")
			.Because("a fully-visual inherited leaf must still be listed through the parent chain");

		var result = await Eval(viewer.Handle, $"lattrp({child.DbRef}/VD{uid}`PUB)");
		await Assert.That(result).DoesNotContain($"VD{uid}`PUB")
			.Because("visual on the inherited leaf alone does not grant access - the parent's branch must be visual too");
	}

	/// <summary>
	/// Characterisation: <c>get()</c> resolves the whole path through
	/// <c>GetAttributeWithInheritanceQuery</c>, which has always run on the source object, so it
	/// already denied this. Pinned so the pattern path and the direct path cannot drift apart
	/// again.
	/// </summary>
	[Test]
	public async ValueTask MortalDarkBranchOnParent_AlreadyBlocksGet()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SrcGetP");
		var child = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcGetC");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SrcGetV");

		await Cmd(1, $"&GO{uid} {parent}=openbranch");
		await Cmd(1, $"&GO{uid}`PUB {parent}=openleafvalue");
		await Cmd(1, $"@set {parent}/GO{uid}=visual");
		await Cmd(1, $"@set {parent}/GO{uid}`PUB=visual");

		await Cmd(1, $"&GD{uid} {parent}=secretbranch");
		await Cmd(1, $"&GD{uid}`PUB {parent}=secretleafvalue");
		await Cmd(1, $"@set {parent}/GD{uid}=mortal_dark");
		await Cmd(1, $"@set {parent}/GD{uid}`PUB=visual");

		await Cmd(1, $"@parent {child.DbRef}={parent}");

		var control = await Eval(viewer.Handle, $"get({child.DbRef}/GO{uid}`PUB)");
		await Assert.That(control).IsEqualTo("openleafvalue")
			.Because("a fully-visual inherited leaf is readable through the parent chain");

		var result = await Eval(viewer.Handle, $"get({child.DbRef}/GD{uid}`PUB)");
		await Assert.That(result).DoesNotContain("secretleafvalue")
			.Because("get() resolves the path on the source object and must deny the mortal_dark branch");
	}
}
