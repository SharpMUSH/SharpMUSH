using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <c>Controls()</c> through guarded roles: a role that allows <c>protect.role</c> refuses a controller without
/// <c>control.role.&lt;role&gt;</c>. With the built-in defaults that is PennMUSH's <c>controls()</c>: nobody controls #1, only
/// wizards control a wizard, only royalty and wizards an object of royalty. TRUST and MISTRUST decide after the guards,
/// as they do after PennMUSH's privilege tests. Each test asks <c>controls()</c> as #1 about players and things of its own.
/// </summary>
public class RoleGuardControlTests : ServerTestBase
{
	private async Task<DBRef> PlayerAsync(string prefix, string? role = null)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);
		if (role is not null) await Cmd($"@role/assign #{player.DbRef.Number}={role}");
		return player.DbRef;
	}

	private async Task<(DBRef Player, DBRef Thing)> PlayerWithThingAsync(string prefix, string? role = null)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);
		if (role is not null) await Cmd($"@role/assign #{player.DbRef.Number}={role}");
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName(prefix + "Thing"), player.Handle);
		return (player.DbRef, Created(created));
	}

	private static DBRef Created(CallState created)
		=> DBRef.TryParse(created.Message.ToPlainText()!.Trim(), out var made) && made is { } dbref
			? dbref
			: throw new InvalidOperationException($"Creating an object answered {created.Message}.");

	private async Task<bool> ControlsAsync(DBRef who, DBRef what)
		=> await Eval($"controls(#{who.Number},#{what.Number})") == "1";

	[Test]
	public async Task NobodyButGodControlsGod()
	{
		var wizard = await PlayerAsync("GuardWizGod", BuiltInRoles.WizardSlug);

		await Assert.That(await ControlsAsync(wizard, new DBRef(1))).IsFalse();
		await Assert.That(await ControlsAsync(new DBRef(1), wizard)).IsTrue();
	}

	[Test]
	public async Task AWizardControlsAnotherWizardAsInPennMUSH()
	{
		var wizard = await PlayerAsync("GuardWizA", BuiltInRoles.WizardSlug);
		var other = await PlayerAsync("GuardWizB", BuiltInRoles.WizardSlug);

		await Assert.That(await ControlsAsync(wizard, other)).IsTrue();
	}

	[Test]
	public async Task RoyaltyControlsNoWizardAndNoOtherRoyalty()
	{
		var royalty = await PlayerAsync("GuardRoyA", BuiltInRoles.RoyaltySlug);
		var wizard = await PlayerAsync("GuardRoyWiz", BuiltInRoles.WizardSlug);
		var other = await PlayerAsync("GuardRoyB", BuiltInRoles.RoyaltySlug);

		await Assert.That(await ControlsAsync(royalty, wizard)).IsFalse();
		await Assert.That(await ControlsAsync(royalty, other)).IsFalse()
			.Because("passing royalty's guard only lets ownership decide, and a player owns only itself");
		await Assert.That(await ControlsAsync(wizard, royalty)).IsTrue();
	}

	/// <summary>The guard reads the target's own roles: a thing of royalty's that holds none is not guarded.</summary>
	[Test]
	public async Task AControlLockOpensAnUnguardedThingButNotAGuardedOne()
	{
		var mortal = await PlayerAsync("GuardLockMortal");
		var (_, plain) = await PlayerWithThingAsync("GuardLockPlain", BuiltInRoles.RoyaltySlug);
		var (_, guarded) = await PlayerWithThingAsync("GuardLockRoyal", BuiltInRoles.RoyaltySlug);
		await Cmd($"@lock/control #{plain.Number}=#{mortal.Number}");
		await Cmd($"@lock/control #{guarded.Number}=#{mortal.Number}");
		await Cmd($"@role/assign #{guarded.Number}={BuiltInRoles.RoyaltySlug}");

		await Assert.That(await ControlsAsync(mortal, plain)).IsTrue();
		await Assert.That(await ControlsAsync(mortal, guarded)).IsFalse();
	}

	/// <summary>
	/// PennMUSH's TRUST: a player's TRUST thing controls its owner, unless the owner holds privilege the thing lacks
	/// (<c>Hasprivs(what) &amp;&amp; !Hasprivs(who)</c>), which is royalty's guard here.
	/// </summary>
	[Test]
	public async Task ATrustThingControlsAMortalOwnerButNotARoyalOne()
	{
		var (mortal, mortalsThing) = await PlayerWithThingAsync("GuardTrustMortal");
		var (royalty, royaltysThing) = await PlayerWithThingAsync("GuardTrustRoyal", BuiltInRoles.RoyaltySlug);

		await Assert.That(await ControlsAsync(mortalsThing, mortal)).IsFalse().Because("without TRUST a thing controls no player");

		await Cmd($"@set #{mortalsThing.Number}=TRUST");
		await Cmd($"@set #{royaltysThing.Number}=TRUST");

		await Assert.That(await ControlsAsync(mortalsThing, mortal)).IsTrue();
		await Assert.That(await ControlsAsync(royaltysThing, royalty)).IsFalse();
		await Assert.That(await ControlsAsync(royalty, royaltysThing)).IsTrue();
	}

	[Test]
	public async Task AMistrustThingControlsNothingElseOfItsOwners()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "GuardMistrust");
		var first = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName("GuardMistrustA"), player.Handle);
		var second = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName("GuardMistrustB"), player.Handle);
		var a = Created(first);
		var b = Created(second);

		await Assert.That(await ControlsAsync(a, b)).IsTrue();
		await Cmd($"@set #{a.Number}=MISTRUST");
		await Assert.That(await ControlsAsync(a, b)).IsFalse();
	}

	/// <summary>
	/// A game's own guarded role stops <c>control.all</c> until a role passes its guard, which is how RhostMUSH's
	/// immortal stays out of a wizard's reach.
	/// </summary>
	[Test]
	public async Task AGuardedRoleOfTheGamesOwnStopsAWizardUntilARolePassesIt()
	{
		var guarded = "g" + Guid.NewGuid().ToString("N")[..12];
		var keeper = "k" + Guid.NewGuid().ToString("N")[..12];
		await Cmd($"@role/create {guarded}=Staff");
		await Cmd($"@role/allow {guarded}=protect.role");
		await Cmd($"@role/create {keeper}=Staff");
		await Cmd($"@role/allow {keeper}=control.role.{guarded}");

		var holder = await PlayerAsync("GuardOwnHolder", guarded);
		var wizard = await PlayerAsync("GuardOwnWizard", BuiltInRoles.WizardSlug);

		await Assert.That(await ControlsAsync(wizard, holder)).IsFalse();

		await Cmd($"@role/assign #{wizard.Number}={keeper}");
		await Assert.That(await ControlsAsync(wizard, holder)).IsTrue();

		await Cmd($"@role/delete {keeper}");
		await Cmd($"@role/delete {guarded}");
	}

	/// <summary>A wizard cannot put itself beyond its peers by giving itself a guarded role no wizard passes.</summary>
	[Test]
	public async Task AWizardCannotShieldItselfWithAGuardedRole()
	{
		var guarded = "g" + Guid.NewGuid().ToString("N")[..12];
		await Cmd($"@role/create {guarded}=Staff");
		await Cmd($"@role/allow {guarded}=protect.role");

		var shielded = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "GuardSelfWizard");
		await Cmd($"@role/assign #{shielded.DbRef.Number}={BuiltInRoles.WizardSlug}");
		var peer = await PlayerAsync("GuardSelfPeer", BuiltInRoles.WizardSlug);

		await CommandParser.CommandParse(shielded.Handle, ConnectionService, MarkupText.Plain($"@role/assign me={guarded}"));
		await Assert.That(await ControlsAsync(peer, shielded.DbRef)).IsTrue();

		await Cmd($"@role/delete {guarded}");
	}
}
