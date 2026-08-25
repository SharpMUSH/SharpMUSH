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
		await Assert.That(result).IsNotEqualTo("leafvalue")
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
}
