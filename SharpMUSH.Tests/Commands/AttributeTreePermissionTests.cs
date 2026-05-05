using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for attribute tree permission enforcement.
/// Covers testatree.t perms section.
/// </summary>
public class AttributeTreePermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// A mortal can set attributes on their own object normally.
	/// </summary>
	[Test]
	public async ValueTask Mortal_CanSetOwnAttributes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermMortal");

		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PM{uid} me=baz"));
		var g1 = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PM{uid})"));
		await Assert.That(g1!.Message!.ToPlainText()).IsEqualTo("baz");

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PM{uid}`BAR me=baz"));
		var g2 = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PM{uid}`BAR)"));
		await Assert.That(g2!.Message!.ToPlainText()).IsEqualTo("baz");

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PM{uid}`BAR`DEEP me=baz"));
		var g3 = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PM{uid}`BAR`DEEP)"));
		await Assert.That(g3!.Message!.ToPlainText()).IsEqualTo("baz");
	}

	/// <summary>
	/// When a branch attribute has the wiz flag, a mortal cannot overwrite it.
	/// </summary>
	[Test]
	public async ValueTask WizFlag_PreventsMortalOverwrite()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermWiz");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PW{uid} me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PW{uid}`BAR me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PW{uid}`BAR`DEEP me=baz"));

		// God sets wiz flag on the branch
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PW{uid}`BAR=wizard"));

		// Mortal cannot overwrite wiz attribute
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PW{uid}`BAR me=newval"));

		// Value should not have changed
		var getResult = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PW{uid}`BAR)"));
		await Assert.That(getResult!.Message!.ToPlainText()).IsEqualTo("baz")
			.Because("mortal should not be able to overwrite wiz-flagged attribute");
	}

	/// <summary>
	/// When a branch attribute has the wiz flag, a mortal cannot create children under it.
	/// </summary>
	[Test]
	public async ValueTask WizFlag_PreventsMortalChildCreation()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermWizChild");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PC{uid} me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PC{uid}`BAR me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PC{uid}`BAR`DEEP me=baz"));

		// God sets wiz flag on the branch
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PC{uid}`BAR=wizard"));

		// Mortal cannot modify existing child under wiz branch
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PC{uid}`BAR`DEEP me=newval"));
		var getDeep = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PC{uid}`BAR`DEEP)"));
		await Assert.That(getDeep!.Message!.ToPlainText()).IsEqualTo("baz")
			.Because("mortal should not be able to modify child of wiz-flagged attribute");

		// Mortal cannot create new child under wiz branch
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PC{uid}`BAR`NEWCHILD me=val"));
		var getNew = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PC{uid}`BAR`NEWCHILD)"));
		await Assert.That(getNew!.Message!.ToPlainText()).IsEqualTo("")
			.Because("mortal should not be able to create child under wiz-flagged attribute");
	}

	/// <summary>
	/// mortal_dark flag on a branch prevents mortal from seeing it via get().
	/// God can still see it.
	/// </summary>
	[Test]
	public async ValueTask MortalDark_HidesFromMortal()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermDark");
		var mortalDbRef = mortal.DbRef.ToString();

		// God creates tree on mortal
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PD{uid} {mortalDbRef}=parent"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PD{uid}`BAR {mortalDbRef}=secret"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PD{uid}`BAR`DEEP {mortalDbRef}=deeper"));

		// Set mortal_dark on the branch
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PD{uid}`BAR=mortal_dark"));

		// Mortal cannot see the dark attribute
		var r1 = await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"think get(me/PD{uid}`BAR)"));
		await Assert.That(r1.Message!.ToPlainText()).DoesNotContain("secret")
			.Because("mortal should not see mortal_dark attribute");

		// God CAN see it
		var r2 = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"think get({mortalDbRef}/PD{uid}`BAR)"));
		await Assert.That(r2.Message!.ToPlainText()).Contains("secret");
	}

	/// <summary>
	/// mortal_dark on a branch also hides children of that branch from mortal.
	/// </summary>
	[Test]
	public async ValueTask MortalDark_HidesChildrenFromMortal()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermDarkC");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDC{uid} {mortalDbRef}=parent"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDC{uid}`BAR {mortalDbRef}=branch"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDC{uid}`BAR`DEEP {mortalDbRef}=hidden"));

		// Set mortal_dark on the branch
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PDC{uid}`BAR=mortal_dark"));

		// Mortal cannot see child of dark attribute
		var r1 = await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"think get(me/PDC{uid}`BAR`DEEP)"));
		await Assert.That(r1.Message!.ToPlainText()).DoesNotContain("hidden")
			.Because("mortal should not see children of mortal_dark attribute");
	}

	/// <summary>
	/// mortal_dark hides the branch from lattr() for mortals.
	/// </summary>
	[Test]
	public async ValueTask MortalDark_HidesFromLattrForMortal()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermDarkL");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDL{uid} {mortalDbRef}=parent"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDL{uid}`BAR {mortalDbRef}=branch"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&PDL{uid}`BAR`DEEP {mortalDbRef}=hidden"));

		// Set mortal_dark on the branch
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PDL{uid}`BAR=mortal_dark"));

		// Mortal's lattr(**) should NOT show dark branch or its children
		var r1 = await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"think lattr(me/**)"));
		var text = r1.Message!.ToPlainText();

		await Assert.That(text).Contains($"PDL{uid}")
			.Because("parent of dark branch should still be visible");
		await Assert.That(text).DoesNotContain($"PDL{uid}`BAR")
			.Because("mortal_dark branch should not appear in lattr for mortal");
	}

	/// <summary>
	/// Removing wiz flag allows mortal to write again, even if mortal_dark remains.
	/// </summary>
	[Test]
	public async ValueTask RemovingWiz_AllowsMortalWriteEvenWithDark()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermWizDark");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PX{uid} me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PX{uid}`BAR me=baz"));

		// God sets both wiz and mortal_dark
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PX{uid}`BAR=wizard"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PX{uid}`BAR=mortal_dark"));

		// Mortal can't write (wiz blocks)
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PX{uid}`BAR me=newval"));
		var getBlocked = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PX{uid}`BAR)"));
		await Assert.That(getBlocked!.Message!.ToPlainText()).IsEqualTo("baz")
			.Because("wiz flag should block mortal write");

		// Remove wiz, mortal_dark stays
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PX{uid}`BAR=!wizard"));

		// Mortal CAN write again (mortal_dark only blocks reading, not writing)
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PX{uid}`BAR me=newval"));
		var getUnblocked = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PX{uid}`BAR)"));
		await Assert.That(getUnblocked!.Message!.ToPlainText()).IsEqualTo("newval")
			.Because("mortal_dark only blocks reading, not writing — removing wiz restores write access");
	}

	/// <summary>
	/// Mortal can still write children under non-wiz mortal_dark branch.
	/// </summary>
	[Test]
	public async ValueTask MortalDark_DoesNotBlockWrite()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PermDarkW");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PDW{uid} me=baz"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PDW{uid}`BAR me=baz"));

		// God sets mortal_dark (but NOT wiz)
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/PDW{uid}`BAR=mortal_dark"));

		// Mortal CAN write (dark only hides, doesn't lock)
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PDW{uid}`BAR me=newval"));
		var getWrite = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PDW{uid}`BAR)"));
		await Assert.That(getWrite!.Message!.ToPlainText()).IsEqualTo("newval")
			.Because("mortal_dark does not prevent writing, only reading");

		// Mortal CAN create children under dark branch
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&PDW{uid}`BAR`CHILD me=val"));
		var getChild = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/PDW{uid}`BAR`CHILD)"));
		await Assert.That(getChild!.Message!.ToPlainText()).IsEqualTo("val")
			.Because("mortal_dark does not prevent creating children");
	}
}
