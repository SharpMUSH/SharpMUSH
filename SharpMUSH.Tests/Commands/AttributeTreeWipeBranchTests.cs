using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for @wipe command and branch attribute behavior, ported from PennMUSH testatree.t.
/// Covers: @wipe clearing entire tree, lattr() tree visibility, get() on tree attributes.
/// </summary>
public class AttributeTreeWipeBranchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	/// <summary>
	/// @wipe should clear an entire attribute tree including all leaves.
	/// PennMUSH testatree.t: atree.basic.14-17
	/// </summary>
	[Test]
	public async ValueTask Wipe_ShouldClearEntireTree()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WipeTree");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Build a small tree: foo, foo`bar, foo`bar`baz
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {objDbRef}=baz"));

		// Wipe foo tree
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@wipe {objDbRef}/FOO"));

		// All three attributes should be gone
		var fooAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO",
			IAttributeService.AttributeMode.Read, false);
		var barAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO`BAR",
			IAttributeService.AttributeMode.Read, false);
		var bazAttr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, "FOO`BAR`BAZ",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(fooAttr.IsAttribute).IsFalse()
			.Because("@wipe should clear the root branch");
		await Assert.That(barAttr.IsAttribute).IsFalse()
			.Because("@wipe should clear intermediate branches");
		await Assert.That(bazAttr.IsAttribute).IsFalse()
			.Because("@wipe should clear leaf nodes");
	}

	/// <summary>
	/// Setting foo`bar without foo existing should auto-create foo as a branch.
	/// PennMUSH testatree.t: atree.branch.1-2
	/// </summary>
	[Test]
	public async ValueTask Branch_SettingLeafAutoCreatesBranch()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "BranchAuto");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Set foo`bar without foo existing
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		// foo should be auto-created
		var result = (await Parser.FunctionParse(MModule.single($"hasattr({objDbRef},FOO)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1")
			.Because("setting foo`bar should auto-create FOO branch");
	}

	/// <summary>
	/// Adding another child should not wipe previous values.
	/// PennMUSH testatree.t: atree.branch.3-4
	/// </summary>
	[Test]
	public async ValueTask Branch_AddingChildPreservesExisting()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "BranchPres");
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Set foo`bar
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=original"));

		// Set foo`bar`baz (deeper)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {objDbRef}=deeper"));

		// foo`bar should still have its value
		var result = (await Parser.FunctionParse(MModule.single($"get({objDbRef}/FOO`BAR)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("original")
			.Because("adding a child should not wipe the parent's value");
	}

	/// <summary>
	/// lattr() shows only top-level attributes by default (not children).
	/// PennMUSH testatree.t: atree.matching.18
	/// </summary>
	[Test]
	public async ValueTask Lattr_ShowsOnlyTopLevel()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LattrTop");

		// Build tree
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAZ {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {objDbRef}=baz"));

		// lattr(obj) should show FOO but not FOO`BAR or FOO`BAR`BAZ
		var result = (await Parser.FunctionParse(MModule.single($"lattr({objDbRef})")))?.Message!;
		var text = result.ToPlainText();

		await Assert.That(text).Contains("FOO")
			.Because("lattr should show top-level attributes");
		await Assert.That(text).DoesNotContain("FOO`BAR")
			.Because("lattr should not show child attributes by default");
	}

	/// <summary>
	/// lattr(obj/**) shows the full tree recursively.
	/// PennMUSH testatree.t: atree.matching.19
	/// </summary>
	[Test]
	public async ValueTask Lattr_DoubleStarShowsFullTree()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LattrStar");

		// Build tree
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {objDbRef}=baz"));

		// lattr(obj/**) should show everything
		var result = (await Parser.FunctionParse(MModule.single($"lattr({objDbRef}/**)")))?.Message!;
		var text = result.ToPlainText();

		await Assert.That(text).Contains("FOO")
			.Because("lattr/**) should show root");
		await Assert.That(text).Contains("FOO`BAR")
			.Because("lattr/**) should show intermediate");
		await Assert.That(text).Contains("FOO`BAR`BAZ")
			.Because("lattr/**) should show leaves");
	}

	/// <summary>
	/// get() retrieves attribute values through the tree.
	/// PennMUSH testatree.t: atree.branch.4
	/// </summary>
	[Test]
	public async ValueTask Get_RetrievesTreeValues()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GetTree");

		// Build tree with values
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}=root"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=middle"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {objDbRef}=leaf"));

		var rootResult = (await Parser.FunctionParse(MModule.single($"get({objDbRef}/FOO)")))?.Message!;
		var midResult = (await Parser.FunctionParse(MModule.single($"get({objDbRef}/FOO`BAR)")))?.Message!;
		var leafResult = (await Parser.FunctionParse(MModule.single($"get({objDbRef}/FOO`BAR`BAZ)")))?.Message!;

		await Assert.That(rootResult.ToPlainText()).IsEqualTo("root");
		await Assert.That(midResult.ToPlainText()).IsEqualTo("middle");
		await Assert.That(leafResult.ToPlainText()).IsEqualTo("leaf");
	}

	/// <summary>
	/// flags(obj/foo) should show the backtick flag for branch attributes.
	/// PennMUSH testatree.t: atree.matching.20
	/// KNOWN GAP: Branch flag (`) exists in DB schema but logic to set/unset it is not wired up.
	/// </summary>
	[Test]
	public async ValueTask Flags_ShowsBacktickForBranch()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "FlagsTree");

		// Build tree
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {objDbRef}=baz"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {objDbRef}=baz"));

		// flags(obj/foo) should include ` because foo is a branch
		var result = (await Parser.FunctionParse(MModule.single($"flags({objDbRef}/FOO)")))?.Message!;
		var text = result.ToPlainText();

		await Assert.That(text).Contains("`")
			.Because("branch attributes should have the backtick flag");
	}

	/// <summary>
	/// When the last child of a branch attribute is cleared, the branch flag should be removed.
	/// PennMUSH: &FOO`BAR obj=val, then &FOO`BAR obj= removes the child, and flags(obj/FOO) no longer shows `.
	/// </summary>
	[Test]
	public async ValueTask BranchFlag_RemovedWhenLastChildCleared()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"BfRm_{uid}");
		var d = $"#{dbRef.Number}";

		// Create branch: FOO with child BAR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BF{uid} {d}=parent_val"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BF{uid}`CHILD {d}=child_val"));

		// Verify branch flag is set
		var flagsBefore = (await Parser.FunctionParse(MModule.single($"flags({d}/BF{uid})")))?.Message!.ToPlainText();
		await Assert.That(flagsBefore).Contains("`");

		// Wipe the only child
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@wipe {d}/BF{uid}`CHILD"));

		// Branch flag should be removed since FOO has no more children
		var flagsAfter = (await Parser.FunctionParse(MModule.single($"flags({d}/BF{uid})")))?.Message!.ToPlainText();
		await Assert.That(flagsAfter).DoesNotContain("`")
			.Because("branch flag should be removed when last child is cleared");
	}

	/// <summary>
	/// Branch flag removed via &attr= (clear to empty on leaf).
	/// </summary>
	[Test]
	public async ValueTask BranchFlag_RemovedWhenLastChildClearedViaAmpersand()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"BfCl_{uid}");
		var d = $"#{dbRef.Number}";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BC{uid} {d}=parent_val"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BC{uid}`CHILD {d}=child_val"));

		var flagsBefore = (await Parser.FunctionParse(MModule.single($"flags({d}/BC{uid})")))?.Message!.ToPlainText();
		await Assert.That(flagsBefore).Contains("`");

		// Clear via &attr obj (no =) which clears the attribute
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BC{uid}`CHILD {d}"));

		var flagsAfter = (await Parser.FunctionParse(MModule.single($"flags({d}/BC{uid})")))?.Message!.ToPlainText();
		await Assert.That(flagsAfter).DoesNotContain("`")
			.Because("branch flag should be removed when leaf child is cleared via &attr=");
	}

	/// <summary>
	/// Branch flag should remain if there are still other children after one is removed.
	/// </summary>
	[Test]
	public async ValueTask BranchFlag_RemainsWhenOtherChildrenExist()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"BfK_{uid}");
		var d = $"#{dbRef.Number}";

		// Create branch: FOO with two children
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BK{uid} {d}=parent_val"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BK{uid}`ONE {d}=val1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BK{uid}`TWO {d}=val2"));

		// Clear one child
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BK{uid}`ONE {d}="));

		// Branch flag should still be present (TWO still exists)
		var flags = (await Parser.FunctionParse(MModule.single($"flags({d}/BK{uid})")))?.Message!.ToPlainText();
		await Assert.That(flags).Contains("`")
			.Because("branch flag should remain while other children exist");
	}
}
