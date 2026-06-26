using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for attribute tree matching with wildcards and patterns.
/// Based on testatree.t matching section.
/// </summary>
public class AttributeTreeMatchingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task<string> Eval(string expr)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		return result?.Message?.ToPlainText() ?? "";
	}

	private async Task Cmd(string cmd)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmd));
	}

	/// <summary>
	/// lattr(obj/*) returns top-level attributes matching wildcard.
	/// </summary>
	[Test]
	public async ValueTask Lattr_Wildcard_TopLevel()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchWild");
		var dbref = thing.ToString();

		await Cmd($"&M{uid}A {dbref}=1");
		await Cmd($"&M{uid}B {dbref}=2");
		await Cmd($"&M{uid}C {dbref}=3");
		await Cmd($"&OTHER{uid} {dbref}=4");

		var result = await Eval($"lattr({dbref}/M{uid}*)");
		await Assert.That(result).Contains($"M{uid}A");
		await Assert.That(result).Contains($"M{uid}B");
		await Assert.That(result).Contains($"M{uid}C");
		await Assert.That(result).DoesNotContain($"OTHER{uid}");
	}

	/// <summary>
	/// lattr(obj/**) returns all attributes including tree children.
	/// </summary>
	[Test]
	public async ValueTask Lattr_DoubleWild_AllLevels()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchAll");
		var dbref = thing.ToString();

		await Cmd($"&MA{uid} {dbref}=1");
		await Cmd($"&MA{uid}`SUB {dbref}=2");
		await Cmd($"&MA{uid}`SUB`DEEP {dbref}=3");

		var result = await Eval($"lattr({dbref}/MA{uid}**)");
		await Assert.That(result).Contains($"MA{uid}");
		await Assert.That(result).Contains($"MA{uid}`SUB");
		await Assert.That(result).Contains($"MA{uid}`SUB`DEEP");
	}

	/// <summary>
	/// lattr(obj/BRANCH`*) returns direct children of branch.
	/// </summary>
	[Test]
	public async ValueTask Lattr_BranchWildcard_DirectChildren()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchBr");
		var dbref = thing.ToString();

		await Cmd($"&MB{uid} {dbref}=parent");
		await Cmd($"&MB{uid}`A {dbref}=child1");
		await Cmd($"&MB{uid}`B {dbref}=child2");
		await Cmd($"&MB{uid}`A`DEEP {dbref}=grandchild");

		var result = await Eval($"lattr({dbref}/MB{uid}`*)");
		await Assert.That(result).Contains($"MB{uid}`A");
		await Assert.That(result).Contains($"MB{uid}`B");
		await Assert.That(result).DoesNotContain($"MB{uid}`A`DEEP")
			.Because("single wildcard should not match grandchildren");
	}

	/// <summary>
	/// lattr(obj/BRANCH`**) returns all descendants of branch.
	/// </summary>
	[Test]
	public async ValueTask Lattr_BranchDoubleWild_AllDescendants()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchBrAll");
		var dbref = thing.ToString();

		await Cmd($"&MD{uid} {dbref}=parent");
		await Cmd($"&MD{uid}`A {dbref}=child");
		await Cmd($"&MD{uid}`A`DEEP {dbref}=grandchild");
		await Cmd($"&MD{uid}`B {dbref}=child2");

		var result = await Eval($"lattr({dbref}/MD{uid}`**)");
		await Assert.That(result).Contains($"MD{uid}`A");
		await Assert.That(result).Contains($"MD{uid}`A`DEEP");
		await Assert.That(result).Contains($"MD{uid}`B");
	}

	/// <summary>
	/// nattr() counts only root-level attributes.
	/// </summary>
	[Test]
	public async ValueTask Nattr_CountsRootOnly()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchNattr");
		var dbref = thing.ToString();

		await Cmd($"&MN{uid}A {dbref}=1");
		await Cmd($"&MN{uid}B {dbref}=2");
		await Cmd($"&MN{uid}A`SUB {dbref}=3");
		await Cmd($"&MN{uid}A`SUB`DEEP {dbref}=4");

		var resultAll = await Eval($"lattr({dbref}/MN{uid}**)");
		var countAll = resultAll.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		var resultRoot = await Eval($"lattr({dbref}/MN{uid}*)");
		var countRoot = resultRoot.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		await Assert.That(countRoot).IsEqualTo(2)
			.Because("only 2 root-level attrs with this prefix");
		await Assert.That(countAll).IsEqualTo(4)
			.Because("all levels should have 4 attrs");
	}

	/// <summary>
	/// hasattr() works on tree attributes.
	/// </summary>
	[Test]
	public async ValueTask Hasattr_TreeAttribute()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchHas");
		var dbref = thing.ToString();

		await Cmd($"&MH{uid} {dbref}=parent");
		await Cmd($"&MH{uid}`SUB {dbref}=child");

		var r1 = await Eval($"hasattr({dbref},MH{uid})");
		await Assert.That(r1).IsEqualTo("1");

		var r2 = await Eval($"hasattr({dbref},MH{uid}`SUB)");
		await Assert.That(r2).IsEqualTo("1");

		var r3 = await Eval($"hasattr({dbref},MH{uid}`NONEXIST)");
		await Assert.That(r3).IsEqualTo("0");
	}

	/// <summary>
	/// get() works on tree attributes.
	/// </summary>
	[Test]
	public async ValueTask Get_TreeAttribute()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MatchGet");
		var dbref = thing.ToString();

		await Cmd($"&MG{uid} {dbref}=parentval");
		await Cmd($"&MG{uid}`SUB {dbref}=childval");
		await Cmd($"&MG{uid}`SUB`DEEP {dbref}=deepval");

		var r1 = await Eval($"get({dbref}/MG{uid})");
		await Assert.That(r1).IsEqualTo("parentval");

		var r2 = await Eval($"get({dbref}/MG{uid}`SUB)");
		await Assert.That(r2).IsEqualTo("childval");

		var r3 = await Eval($"get({dbref}/MG{uid}`SUB`DEEP)");
		await Assert.That(r3).IsEqualTo("deepval");
	}
}
