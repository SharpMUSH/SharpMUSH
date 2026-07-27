using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH <c>controls()</c> (<c>predicat.c:416</c>) reads the control lock raw and skips it when it is
/// <c>TRUE_BOOLEXP</c>, so an object with no control lock set is controlled by nobody but its owner.
/// Evaluating the lock instead would inherit the "unset locks pass" convention and hand control of every
/// unlocked object to everyone.
/// </summary>
public class PermissionServiceControlTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPermissionService PermissionService =>
		WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();

	private IConnectionService ConnectionService =>
		WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject> ObjectAt(string dbrefText)
	{
		SharpMUSH.Library.Models.DBRef.TryParse(dbrefText, out var dbref);
		return (await Mediator.Send(new GetObjectNodeQuery(dbref!.Value))).WithoutNone();
	}

	[Test]
	public async ValueTask AMortalDoesNotControlAnotherPlayersObjectWithNoControlLock()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CtrlNoLock");

		var thingName = TestIsolationHelpers.GenerateUniqueName("CtrlNoLockThing");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));

		var thing = await ObjectAt(createResult.Message!.ToPlainText()!.Trim());
		var mortal = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).WithoutNone();

		await Assert.That(await PermissionService.Controls(mortal, thing)).IsFalse();
	}

	[Test]
	public async ValueTask AMortalControlsAnotherPlayersObjectWhenTheControlLockPasses()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CtrlPassLock");

		var thingName = TestIsolationHelpers.GenerateUniqueName("CtrlPassLockThing");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));
		var thingDbRef = createResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@lock/control {thingDbRef}=#{player.DbRef.Number}"));

		var thing = await ObjectAt(thingDbRef);
		var mortal = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).WithoutNone();

		await Assert.That(await PermissionService.Controls(mortal, thing)).IsTrue();
	}

	[Test]
	public async ValueTask AMortalDoesNotControlAnotherPlayersObjectWhenTheControlLockFails()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CtrlFailLock");

		var thingName = TestIsolationHelpers.GenerateUniqueName("CtrlFailLockThing");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));
		var thingDbRef = createResult.Message!.ToPlainText()!.Trim();

		// Locked to God, who is not our mortal.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock/control {thingDbRef}=#1"));

		var thing = await ObjectAt(thingDbRef);
		var mortal = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).WithoutNone();

		await Assert.That(await PermissionService.Controls(mortal, thing)).IsFalse();
	}

	[Test]
	public async ValueTask AnOwnerStillControlsTheirOwnObjectWithNoControlLock()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CtrlOwner");

		var thingName = TestIsolationHelpers.GenerateUniqueName("CtrlOwnerThing");
		var createResult = await Parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@create {thingName}"));

		var thing = await ObjectAt(createResult.Message!.ToPlainText()!.Trim());
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).WithoutNone();

		await Assert.That(await PermissionService.Controls(owner, thing)).IsTrue();
	}

	/// <summary>
	/// An explicitly-set <c>#TRUE</c> control lock is not the same as no lock, and is not meant to be:
	/// PennMUSH parses <c>#TRUE</c> to a real <c>BOOLEXP_BOOL</c> node (<c>boolexp.c:132</c>), which is
	/// distinct from the <c>TRUE_BOOLEXP</c> sentinel that means "no lock stored". <c>controls()</c> skips
	/// only the sentinel, so <c>@lock/control &lt;obj&gt;=#TRUE</c> is how you say "anyone controls this".
	/// </summary>
	[Test]
	public async ValueTask AnExplicitlyTrueControlLockGrantsControlToAnyone()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CtrlExplicitTrue");

		var thingName = TestIsolationHelpers.GenerateUniqueName("CtrlExplicitTrueThing");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {thingName}"));
		var thingDbRef = createResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock/control {thingDbRef}=#TRUE"));

		var thing = await ObjectAt(thingDbRef);
		var mortal = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).WithoutNone();

		await Assert.That(await PermissionService.Controls(mortal, thing)).IsTrue();
	}
}
