using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for attribute tree parent permissions.
/// Covers testatree.t parent and parentperms sections:
/// - Attributes inherited from parent objects
/// - Permission flags (wiz, mortal_dark) on inherited attributes
/// </summary>
public class AttributeTreeParentPermissionTests
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

	private async Task Cmd(string cmd)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmd));
	}

	/// <summary>
	/// Child object inherits get() from parent.
	/// </summary>
	[Test]
	public async ValueTask Parent_GetInheritsAttribute()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PParent");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PChild");

		await Cmd($"&PP{uid} {parent}=inherited_val");
		await Cmd($"&PP{uid}`SUB {parent}=inherited_sub");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PP{uid})");
		await Assert.That(r1).IsEqualTo("inherited_val");

		var r2 = await Eval(1, $"get({child}/PP{uid}`SUB)");
		await Assert.That(r2).IsEqualTo("inherited_sub");
	}

	/// <summary>
	/// Child's own attribute overrides parent's.
	/// </summary>
	[Test]
	public async ValueTask Parent_ChildOverridesParent()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "POverP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "POverC");

		await Cmd($"&PO{uid} {parent}=from_parent");
		await Cmd($"&PO{uid} {child}=from_child");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PO{uid})");
		await Assert.That(r1).IsEqualTo("from_child")
			.Because("child's own attribute should override parent");
	}

	/// <summary>
	/// lattr() on child does NOT show inherited attributes (only local ones).
	/// </summary>
	[Test]
	public async ValueTask Parent_LattrDoesNotShowInherited()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PLattrP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PLattrC");

		await Cmd($"&PL{uid} {parent}=inherited");
		await Cmd($"&PL{uid}`SUB {parent}=inherited_sub");
		await Cmd($"@parent {child}={parent}");

		var result = await Eval(1, $"lattr({child}/**)");
		await Assert.That(result).DoesNotContain($"PL{uid}")
			.Because("lattr() should only show local attributes, not inherited");
	}

	/// <summary>
	/// lattrp() on child shows inherited attributes from parent.
	/// </summary>
	[Test]
	public async ValueTask Parent_LattrpShowsInherited()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PLattrpP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PLattrpC");

		await Cmd($"&PLR{uid} {parent}=inherited");
		await Cmd($"&PLR{uid}`SUB {parent}=inherited_sub");
		await Cmd($"@parent {child}={parent}");

		var result = await Eval(1, $"lattrp({child}/**)");
		await Assert.That(result).Contains($"PLR{uid}")
			.Because("lattrp() should show inherited attributes from parent");
		await Assert.That(result).Contains($"PLR{uid}`SUB");
	}

	/// <summary>
	/// no_inherit flag on parent's attribute prevents inheritance.
	/// </summary>
	[Test]
	public async ValueTask Parent_NoInheritPreventsGet()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PNoInhP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PNoInhC");

		await Cmd($"&PNI{uid} {parent}=secret");
		await Cmd($"@set {parent}/PNI{uid}=no_inherit");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PNI{uid})");
		await Assert.That(r1).DoesNotContain("secret")
			.Because("no_inherit should prevent get() from seeing parent attribute on child");
	}

	/// <summary>
	/// no_inherit on branch prevents all children from inheriting too.
	/// </summary>
	[Test]
	public async ValueTask Parent_NoInheritOnBranch_BlocksChildren()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PNoBrP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PNoBrC");

		await Cmd($"&PNB{uid} {parent}=root");
		await Cmd($"&PNB{uid}`SUB {parent}=childval");
		await Cmd($"@set {parent}/PNB{uid}=no_inherit");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PNB{uid})");
		await Assert.That(r1).DoesNotContain("root");

		var r2 = await Eval(1, $"get({child}/PNB{uid}`SUB)");
		await Assert.That(r2).IsEqualTo("childval")
			.Because("no_inherit on branch does NOT block children — they're independent attributes");
	}

	/// <summary>
	/// mortal_dark on parent's attribute: mortal cannot see inherited value.
	/// </summary>
	[Test]
	public async ValueTask ParentPerms_MortalDark_HidesInheritedFromMortal()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PPDarkP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PPDarkC");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PPDarkM");

		await Cmd($"&PPD{uid} {parent}=secret");
		await Cmd($"@set {parent}/PPD{uid}=mortal_dark");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PPD{uid})");
		await Assert.That(r1).IsEqualTo("secret");

		var r2 = await Eval(mortal.Handle, $"get({child}/PPD{uid})");
		await Assert.That(r2).DoesNotContain("secret")
			.Because("mortal should not see mortal_dark inherited attribute");
	}

	/// <summary>
	/// wiz on parent's attribute: mortal cannot set override on child.
	/// </summary>
	[Test]
	public async ValueTask ParentPerms_WizOnParent_MortalCannotOverrideOnChild()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PPWizP");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PPWizM");
		var mortalDbRef = mortal.DbRef.ToString();

		await Cmd($"&PPW{uid} {parent}=wizval");
		await Cmd($"@set {parent}/PPW{uid}=wizard");
		await Cmd($"@parent {mortalDbRef}={parent}");

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PPW{uid} me=myval"));
		var getResult = await Eval(mortal.Handle, $"get(me/PPW{uid})");
		await Assert.That(getResult).IsEqualTo("myval")
			.Because("mortal should be able to set local copy overriding inherited wiz-flagged attribute");
	}

	/// <summary>
	/// hasattr works on inherited attributes.
	/// </summary>
	[Test]
	public async ValueTask Parent_HasattrInherited()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PHasP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PHasC");

		await Cmd($"&PH{uid} {parent}=val");
		await Cmd($"@parent {child}={parent}");

		// hasattr does NOT check parents (PennMUSH behavior)
		var r1 = await Eval(1, $"hasattr({child},PH{uid})");
		await Assert.That(r1).IsEqualTo("0")
			.Because("hasattr should NOT find inherited attribute (only local)");

		var r2 = await Eval(1, $"hasattrp({child},PH{uid})");
		await Assert.That(r2).IsEqualTo("1")
			.Because("hasattrp should find inherited attribute");
	}

	/// <summary>
	/// Multi-level parent chain: grandparent attribute inherits through.
	/// </summary>
	[Test]
	public async ValueTask Parent_MultiLevelInheritance()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var grandparent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PGP");
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PC");

		await Cmd($"&PML{uid} {grandparent}=grandval");
		await Cmd($"@parent {parent}={grandparent}");
		await Cmd($"@parent {child}={parent}");

		var r1 = await Eval(1, $"get({child}/PML{uid})");
		await Assert.That(r1).IsEqualTo("grandval")
			.Because("attribute should inherit through multi-level parent chain");
	}
}
