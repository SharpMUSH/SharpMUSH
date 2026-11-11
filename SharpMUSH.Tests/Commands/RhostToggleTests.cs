using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// RhostMUSH's toggles are flags here. MONITOR is HEAR_CONNECT and KEEPALIVE SharpMUSH's own, so both are toggles on
/// every game; the others are defined by a RhostMUSH import, which these tests stand in for.
/// </summary>
public class RhostToggleTests : ServerTestBase
{
	private async Task<DBRef> ThingAsync()
	{
		await DefineAsync("VPAGE");
		return DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("Toggle")}"));
	}

	private static readonly SemaphoreSlim Defining = new(1, 1);

	/// <summary>Defines a toggle's flag as a RhostMUSH import does: no letter, on any type.</summary>
	private async Task DefineAsync(string name, params string[] permissions)
	{
		await Defining.WaitAsync();
		try
		{
			if (await Mediator.Send(new GetObjectFlagQuery(name)) is null)
			{
				await Mediator.Send(new CreateObjectFlagCommand(name, null, "", false, permissions,
					[.. permissions.Where(permission => permission != "mdark")], ["PLAYER", "THING", "ROOM", "EXIT"]));
			}
		}
		finally
		{
			Defining.Release();
		}
	}

	[Test]
	public async Task ToggleCommandSetsAndClearsTheFlag()
	{
		var thing = await ThingAsync();

		await Assert.That(await Eval($"hastoggle({thing},VPAGE)")).IsEqualTo("0");

		await Cmd($"@toggle {thing}=VPAGE MONITOR");

		await Assert.That(await Eval($"hastoggle({thing},vpage)[hastoggle({thing},MONITOR)]")).IsEqualTo("11");
		await Assert.That(await Eval($"hasflag({thing},HEAR_CONNECT)")).IsEqualTo("1");
		await Assert.That(await Eval($"ltoggles({thing})")).IsEqualTo("MONITOR VPAGE");

		await Cmd($"@toggle {thing}=!MONITOR");

		await Assert.That(await Eval($"ltoggles({thing})")).IsEqualTo("VPAGE");
		await Assert.That(await Eval($"hasflag({thing},HEAR_CONNECT)")).IsEqualTo("0");
	}

	[Test]
	public async Task ClearRemovesEveryToggle()
	{
		var thing = await ThingAsync();
		await Cmd($"@toggle {thing}=VPAGE MONITOR");
		await Cmd($"@set {thing}=SAFE");

		await Cmd($"@toggle/clear {thing}");

		await Assert.That(await Eval($"ltoggles({thing})")).IsEqualTo("");
		await Assert.That(await Eval($"hasflag({thing},SAFE)")).IsEqualTo("1");
	}

	[Test]
	public async Task CheckListsTheToggles()
	{
		var thing = await ThingAsync();
		await Cmd($"@toggle {thing}=VPAGE");

		await Assert.That(await Cmd($"@toggle/check {thing}")).IsEqualTo("VPAGE");
	}

	[Test]
	public async Task ToggleFunctionSetsQuietly()
	{
		var thing = await ThingAsync();

		await Assert.That(await Eval($"toggle({thing},VPAGE)")).IsEqualTo("");
		await Assert.That(await Eval($"hastoggle({thing},VPAGE)")).IsEqualTo("1");

		await Eval($"toggle({thing},!VPAGE)");

		await Assert.That(await Eval($"hastoggle({thing},VPAGE)")).IsEqualTo("0");
	}

	[Test]
	public async Task AnUnknownToggleIsReportedAndTheRestSet()
	{
		var thing = await ThingAsync();
		var unknown = TestIsolationHelpers.GenerateUniqueName("NOSUCH").ToUpperInvariant();

		await Cmd($"@toggle {thing}={unknown} VPAGE");

		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef)
			.Any(message => message.Contains($"no toggle named {unknown}", StringComparison.OrdinalIgnoreCase))).IsTrue();
		await Assert.That(await Eval($"hastoggle({thing},VPAGE)[hastoggle({thing},{unknown})]")).IsEqualTo("10");
	}

	[Test]
	public async Task ListTogglesNamesEachDefinedToggleWithItsLetter()
	{
		await DefineAsync("VPAGE");
		var listing = (await Eval("listtoggles()")).Split(' ');

		await Assert.That(listing).Contains("KEEPALIVE(K)").And.Contains("MONITOR(M)").And.Contains("VPAGE(v)")
			.And.DoesNotContain("CLUSTER(~)");
		await Assert.That(listing).IsOrderedBy(entry => entry, StringComparer.Ordinal);
	}

	[Test]
	public async Task ToggleDefChangesWhoMaySetTheToggle()
	{
		await DefineAsync("NOZONEPARENT");
		await Assert.That(await Mediator.Send(new GetObjectFlagQuery("NOZONEPARENT"))).IsNotNull();

		await Cmd("@toggledef/set NOZONEPARENT=wizard");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOZONEPARENT")))!.SetPermissions)
			.IsEquivalentTo(["wizard"]);

		await Cmd("@toggledef/see NOZONEPARENT=councilor");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOZONEPARENT")))!.SetPermissions)
			.IsEquivalentTo(["wizard", "mdark"]);

		await Cmd("@toggledef/set NOZONEPARENT=mortal");
		await Cmd("@toggledef/see NOZONEPARENT=mortal");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOZONEPARENT")))!.SetPermissions).IsEmpty();
	}

	[Test]
	public async Task ToggleDefLeavesSystemFlagsAndUnknownWordsAlone()
	{
		await DefineAsync("NOGLOBPARENT");

		await Cmd("@toggledef/set MONITOR=mortal");
		await Cmd("@toggledef/set NOGLOBPARENT=g no_such_word");

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("HEAR_CONNECT")))!.SetPermissions)
			.IsEquivalentTo(["royalty"]);
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOGLOBPARENT")))!.SetPermissions).IsEmpty();

		await Cmd("@toggledef/set NOGLOBPARENT=wiz");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOGLOBPARENT")))!.SetPermissions)
			.IsEquivalentTo(["wizard"]);
		await Cmd("@toggledef/set NOGLOBPARENT=m");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("NOGLOBPARENT")))!.SetPermissions).IsEmpty();
	}

	[Test]
	public async Task AMortalNeitherSeesNorClearsAHiddenToggle()
	{
		await DefineAsync("SNUFFDARK", "wizard", "mdark");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "ToggleMortal");
		await Cmd($"@toggle {mortal.DbRef}=SNUFFDARK");

		await Assert.That(await EvalAs(mortal.DbRef, "hastoggle(me,SNUFFDARK)[words(ltoggles(me))]")).IsEqualTo("00");
		await Assert.That(await EvalAs(mortal.DbRef, "listtoggles()")).DoesNotContain("SNUFFDARK");
		await Assert.That(await Eval("listtoggles()")).Contains("SNUFFDARK(u)");

		await CmdAs(mortal.DbRef, mortal.Handle, "@toggle/clear me");

		await Assert.That(await Eval($"hastoggle({mortal.DbRef},SNUFFDARK)")).IsEqualTo("1");
		var heard = string.Join("\n", Notifications.For(mortal.DbRef));
		await Assert.That(heard).Contains("@toggle: all toggles cleared.").And.DoesNotContain("SNUFFDARK");
	}

	[Test]
	public async Task AMortalMayNotUseToggleDefOrCheckWhatItDoesNotControl()
	{
		await DefineAsync("ZONECMDCHK");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "ToggleDefMortal");

		await CmdAs(mortal.DbRef, mortal.Handle, "@toggledef/set ZONECMDCHK=god");

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("ZONECMDCHK")))!.SetPermissions).IsEmpty();
		await Assert.That(await CmdAs(mortal.DbRef, mortal.Handle, "@toggle/check #1")).IsEqualTo("#-1 PERMISSION DENIED");
	}
	private static IDisposable Rhost() => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = "rhost" }
	});

	private bool GodHeard(string text)
		=> WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Any(line => line.Contains(text, StringComparison.Ordinal));

	[Test]
	public async Task FlagDefChangesWhoMaySetAFlagAndListsIt()
	{
		await DefineAsync("SNUFFED");

		using (Rhost())
		{
			await Cmd("@flagdef/set SNUFFED=wiz");
			await Cmd("@flagdef/unset SNUFFED=arch");
			await Cmd("@flagdef SNUFFED");
		}

		var flag = (await Mediator.Send(new GetObjectFlagQuery("SNUFFED")))!;
		await Assert.That(flag.SetPermissions).IsEquivalentTo(["wizard"]);
		await Assert.That(flag.UnsetPermissions).IsEquivalentTo(["royalty"]);
		await Assert.That(GodHeard("@flagdef: SNUFFED updated.")).IsTrue();
		await Assert.That(GodHeard("SNUFFED() set: wizard unset: royalty")).IsTrue();
	}

	[Test]
	public async Task FlagDefRefusesSystemFlagsAndNamesItDoesNotMatchExactly()
	{
		await DefineAsync("FLAGDEFEXACT");
		var wizard = (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!.SetPermissions;

		await Cmd("@rhost_flagdef/set WIZARD=mortal");
		await Cmd("@rhost_flagdef/set FLAGDEFEXA=wiz");

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!.SetPermissions).IsEquivalentTo(wizard);
		await Assert.That(GodHeard("Cannot modify system flag 'WIZARD'.")).IsTrue();
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery("FLAGDEFEXACT")))!.SetPermissions).IsEmpty();
		await Assert.That(GodHeard("@flagdef: there is no flag named FLAGDEFEXA.")).IsTrue();
	}

	[Test]
	public async Task FlagDefListsAFlagByItsRhostName()
	{
		await Cmd("@rhost_flagdef TELOK");

		await Assert.That(WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef)
			.Any(line => line.StartsWith("JUMP_OK(", StringComparison.Ordinal))).IsTrue();
	}
}
