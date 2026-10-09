using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;

namespace SharpMUSH.Tests.Commands;

public class ZoneCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// Creates a fresh, isolated player through the database layer so zone tests never
	/// mutate the shared player #1 object.
	/// </summary>
	private Task<DBRef> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, namePrefix);

	/// <summary>
	/// Creates a fresh player with a registered connection handle so that
	/// <c>Parser.CommandParse(testPlayer.Handle, …)</c> executes as that player.
	/// </summary>
	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerWithHandleAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask ChzoneSetZone()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMaster");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);
		var zoneObject = (await Mediator.Send(new GetObjectNodeQuery(zoneDbRef))).Expect<AnySharpObject>();

		await Assert.That(zoneObject.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);

		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);
		var zonedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		await Assert.That(zonedObject.Object().DBRef.Number).IsEqualTo(objDbRef.Number);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

		var updatedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = (await updatedObject.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();

		await Assert.That(zone.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	/// <summary>
	/// PennMUSH's <c>do_chzone</c> has exactly one success report, <c>"Zone changed."</c>
	/// (<c>src/set.c:487</c>), and reaches it for <c>=none</c> too: the <c>zone != NOTHING</c> guards
	/// above it cover the destination gate and the flag strip, not the notify. "Zone cleared." was a
	/// SharpMUSH invention, and it was written as a raw string rather than a resource key.
	/// </summary>
	[Test]
	public async ValueTask ChzoneClearZone()
	{
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_ClearZone");

		// Create unique zone master object as the fresh player (they own it → controls check passes)
		var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMasterClear");
		var zoneResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedClearObject");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(zonedObject.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		var withZone = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zoneCheck = await withZone.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}=none"));

		// Pattern C: freshPlayer.DbRef is unique to this test so the key match is unambiguous.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.ZoneChanged), freshPlayer.DbRef, freshPlayer.DbRef)).IsTrue();
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "Zone cleared.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);

		var updatedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = await updatedObject.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsTrue();
	}

	/// <summary>
	/// <c>do_chzone</c>'s no-op guard (<c>src/set.c:392-396</c>): re-zoning an object to the zone it is
	/// already in says so and returns 0, which is also <c>do_chzoneall</c>'s <c>Zone(i) != zone</c>
	/// filter (<c>src/wiz.c:1047</c>). SharpMUSH re-ran the whole change, stripping the object's powers
	/// a second time and reporting "Zone changed.".
	/// </summary>
	[Test]
	public async ValueTask ChzoneRefusesAnObjectAlreadyInThatZone()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_AlreadyZoned");
		var zone = await CreateOwnedBy(owner, "AlreadyZonedZone");
		var victim = await CreateOwnedBy(owner, "AlreadyZonedVictim");

		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		var moved = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		var moveZone = (await moved.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(moveZone.Object().DBRef.Number).IsEqualTo(zone.Number);

		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		// Pattern C: owner is unique to this test, so the key match is unambiguous.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.ObjectAlreadyInThatZone), owner.DbRef, owner.DbRef)).IsTrue();
	}

	/// <summary>
	/// <c>do_chzone</c> discards <c>/preserve</c> for anyone who is not a wizard —
	/// <c>if (!Wizard(player)) preserve = 0;</c> (<c>src/set.c:470-471</c>) — so a mortal cannot keep an
	/// object's WIZARD/ROYALTY/TRUST flags or its powers across a zone change. SharpMUSH honoured the
	/// switch for everyone, which is a mortal handing a zone an object that kept every power it had.
	/// </summary>
	[Test]
	public async ValueTask ChzonePreserveIsWizardOnly()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_MortalPreserve");
		var zone = await CreateOwnedBy(owner, "MortalPreserveZone");
		var victim = await CreateOwnedBy(owner, "MortalPreserveVictim");

		// The scenario stands on the executor being a mortal who controls both objects.
		var ownerObj = (await Mediator.Send(new GetObjectNodeQuery(owner.DbRef))).Expect<AnySharpObject>();
		await Assert.That(await ownerObj.IsWizard()).IsFalse();

		// Granted as God: a mortal cannot @power anything.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {victim}=Builder"));
		await Assert.That(await PowerNamesOf(victim)).Contains("Builder");

		await Parser.CommandParse(owner.Handle, ConnectionService,
			MarkupText.Plain($"@chzone/preserve {victim}={zone}"));

		var moved = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		var movedZone = (await moved.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(movedZone.Object().DBRef.Number).IsEqualTo(zone.Number);

		await Assert.That(await PowerNamesOf(victim)).IsEmpty();
	}

	/// <summary>
	/// A refused zone change must not have written anything first. <c>do_chzone</c>'s self-zone guard
	/// (<c>src/set.c:421-426</c>) comes before the flag strip and before <c>check_zone_lock</c>, and
	/// SharpMUSH refuses the self-zone to privileged players too, because
	/// <c>ObjectRelationshipService.SetZone</c> rejects a self-loop as well — exempting a wizard in
	/// the helper would have stripped the object and installed a zone lock on the way to a refusal it
	/// could not avoid.
	/// </summary>
	[Test]
	public async ValueTask ChzoneRefusingASelfZoneWritesNothing()
	{
		var victim = await CreateOwnedBy(await CreateTestPlayerWithHandleAsync("ZT_SelfZone"), "SelfZoneVictim");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {victim}=Builder"));
		await Assert.That(await PowerNamesOf(victim)).Contains("Builder");

		// As God: privileged, and PennMUSH would have let the self-zone through.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {victim}={victim}"));

		var after = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		await Assert.That((await after.Object().Zone.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
		await Assert.That(after.Object().Locks.ContainsKey(nameof(LockType.Zone))).IsFalse();
		await Assert.That(await PowerNamesOf(victim)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@destroy {victim}"));
	}

	/// <summary>
	/// <c>do_chzoneall</c> is a loop calling <c>do_chzone</c> per object with <c>noisy</c> off
	/// (<c>src/wiz.c:1046-1054</c>) — "This keeps consistency on things like flag resetting, etc...".
	/// SharpMUSH's second copy of the loop wrote the zone straight through the Mediator, so
	/// <c>check_zone_lock</c>'s default <c>=me</c> lock (<c>src/lock.c:962</c>) was never installed and
	/// the summary was a per-owner string PennMUSH does not have.
	/// </summary>
	[Test]
	public async ValueTask ChzoneAllRunsTheSameRoutineAsChzone()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_ChzoneAllOwner");
		var zone = await CreateOwnedBy(owner, "ChzoneAllZone");
		var victim = await CreateOwnedBy(owner, "ChzoneAllVictim");

		var zoneBefore = (await Mediator.Send(new GetObjectNodeQuery(zone))).Expect<AnySharpObject>();
		await Assert.That(zoneBefore.Object().Locks.ContainsKey(nameof(LockType.Zone))).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzoneall #{owner.DbRef.Number}={zone}"));

		var moved = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		var movedZone = (await moved.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(movedZone.Object().DBRef.Number).IsEqualTo(zone.Number);

		// check_zone_lock ran, which only happens if the per-object work went through do_chzone.
		var zoneAfter = (await Mediator.Send(new GetObjectNodeQuery(zone))).Expect<AnySharpObject>();
		await Assert.That(zoneAfter.Object().Locks[nameof(LockType.Zone)].LockString)
			.IsEqualTo($"=#{zone.Number}");
	}

	/// <summary>Power names currently granted to an object.</summary>
	private async Task<string[]> PowerNamesOf(DBRef dbref)
	{
		var node = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
		return (await node.Object().ReadPowersAsync(CancellationToken.None)).Select(p => p.Name).ToArray();
	}

	/// <summary>
	/// PennMUSH src/wiz.c do_chzone: zoning a non-player strips its privileged flags and every
	/// power, unless /preserve is given. @CHZONE gets this from the same
	/// FlagAndPowerService.ClearAllPowers that @CHZONEALL uses.
	/// </summary>
	[Test]
	public async ValueTask ChzoneStripsPowers()
	{
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PowerStripZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);

		var objName = TestIsolationHelpers.GenerateUniqueName("PowerStripObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {objDbRef}=Builder Boot"));
		var granted = await PowerNamesOf(objDbRef);
		await Assert.That(granted).Contains("Builder");
		await Assert.That(granted).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(await PowerNamesOf(objDbRef)).IsEmpty();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@destroy {objDbRef}"));
	}

	/// <summary>@CHZONE/PRESERVE keeps the powers the plain command strips.</summary>
	[Test]
	public async ValueTask ChzonePreserveKeepsPowers()
	{
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PowerKeepZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);

		var objName = TestIsolationHelpers.GenerateUniqueName("PowerKeepObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {objDbRef}=Builder"));
		await Assert.That(await PowerNamesOf(objDbRef)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone/preserve {objDbRef}={zoneDbRef}"));

		await Assert.That(await PowerNamesOf(objDbRef)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@destroy {objDbRef}"));
	}

	/// <summary>Creates an object as <paramref name="player"/>, so that player owns it.</summary>
	private async Task<DBRef> CreateOwnedBy(TestIsolationHelpers.TestPlayer player, string namePrefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(namePrefix);
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));
		return DBRef.Parse(result.Message.ToPlainText()!);
	}

	/// <summary>
	/// The executor's control over the object comes only from the zone the object is leaving, so the
	/// zone change invalidates it. PennMUSH's do_chzone (src/set.c:373) strips with
	/// clear_flag_internal() and destroy_flag_bitmask(), neither of which asks permission again: the
	/// controls() it ran once, before "Zone(thing) = zone", is the whole authorization. Re-deriving
	/// permission after the move drops the strip on the floor and still reports success, which is the
	/// object changing hands with its powers intact.
	/// </summary>
	[Test]
	public async ValueTask ChzoneStripsPowersWhenControlCameOnlyFromTheOldZone()
	{
		// Zone Master Object control is what makes this scenario reachable, and the shipped config
		// turns it off. Scoped to this test's async flow; every other test still sees the default.
		using var zmoControl = TestOptionsOverride.Scope(options => options with
		{
			Database = options.Database with { ZoneControlZmpOnly = false }
		});

		var owner = await CreateTestPlayerWithHandleAsync("ZT_ZmoOwner");
		var mover = await CreateTestPlayerWithHandleAsync("ZT_ZmoMover");

		var victim = await CreateOwnedBy(owner, "ZmoVictim");
		var oldZone = await CreateOwnedBy(owner, "ZmoOldZone");
		var newZone = await CreateOwnedBy(owner, "ZmoNewZone");

		// The zone the object sits in is the only thing handing `mover` control of it.
		await Parser.CommandParse(owner.Handle, ConnectionService,
			MarkupText.Plain($"@lock/zone {oldZone}==#{mover.DbRef.Number}"));
		// The destination admits the object through its ChZone lock, and denies `mover` the Zone lock
		// that would have carried control across the move.
		//
		// Set through the Mediator rather than @lock/chzone: @LOCK canonicalises the switch against
		// LockService.SystemLocks, which spells it "Chzone", while every LockType.ChZone read spells
		// it "ChZone" against a case-sensitive lock dictionary. @CHZONE writes "ChZone", so that is
		// the spelling the gate actually reads.
		var newZoneNode = (await Mediator.Send(new GetObjectNodeQuery(newZone))).Expect<AnySharpObject>();
		await Mediator.Send(new SetLockCommand(newZoneNode.Object(), nameof(LockType.ChZone), $"=#{mover.DbRef.Number}", (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>()));
		await Parser.CommandParse(owner.Handle, ConnectionService,
			MarkupText.Plain($"@lock/zone {newZone}==#{owner.DbRef.Number}"));

		await Parser.CommandParse(owner.Handle, ConnectionService,
			MarkupText.Plain($"@chzone {victim}={oldZone}"));

		// Granted after the object is already zoned: the @chzone above would have stripped them.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {victim}=Builder Boot"));
		await Assert.That(await PowerNamesOf(victim)).Contains("Builder");

		// The scenario stands on `mover` controlling the object through the old zone and not through
		// the new one; assert that rather than trusting the setup.
		var permissionService = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var moverObj = (await Mediator.Send(new GetObjectNodeQuery(mover.DbRef))).Expect<AnySharpObject>();
		var victimObj = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		var newZoneObj = (await Mediator.Send(new GetObjectNodeQuery(newZone))).Expect<AnySharpObject>();
		await Assert.That(await permissionService.Controls(moverObj, victimObj)).IsTrue();
		await Assert.That(await permissionService.Controls(moverObj, newZoneObj)).IsFalse();

		await Parser.CommandParse(mover.Handle, ConnectionService,
			MarkupText.Plain($"@chzone {victim}={newZone}"));

		var moved = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		var zone = (await moved.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(zone.Object().DBRef.Number).IsEqualTo(newZone.Number);

		// The move went through, so the powers have to have gone with it.
		await Assert.That(await PowerNamesOf(victim)).IsEmpty();
	}

	[Test]
	public async ValueTask ChzonePermissionSuccess()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PermTestZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		var objName = TestIsolationHelpers.GenerateUniqueName("PermTestObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Try to set zone - this should work since player controls both
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

		var updated = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = await updated.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone.IsNone).IsFalse();
	}

	/// <summary>
	/// PennMUSH <c>do_chzone</c> (<c>src/set.c:408-420</c>): a non-controlling player needs
	/// <c>has_lock &amp;&amp; eval_lock_with(...)</c>, where <c>has_lock</c> is
	/// <c>getlock(zone, Chzone_Lock) != TRUE_BOOLEXP</c> — "Note that an object with no chzone-lock
	/// isn't valid". An unset lock evaluates <c>#TRUE</c>, so gating on the verdict alone handed
	/// every never-zone-locked object to every mortal as a zone.
	/// </summary>
	[Test]
	public async ValueTask ChzoneRefusesAZoneThatCarriesNoChzoneLock()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_NoLockOwner");
		var mover = await CreateTestPlayerWithHandleAsync("ZT_NoLockMover");

		var zone = await CreateOwnedBy(owner, "NoLockZone");
		var victim = await CreateOwnedBy(mover, "NoLockVictim");

		// The scenario stands on the zone having no chzone-lock at all.
		var zoneNode = (await Mediator.Send(new GetObjectNodeQuery(zone))).Expect<AnySharpObject>();
		await Assert.That(zoneNode.Object().Locks.ContainsKey(nameof(LockType.ChZone))).IsFalse();

		// …and on `mover` controlling the object but not the zone.
		var permissionService = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var moverObj = (await Mediator.Send(new GetObjectNodeQuery(mover.DbRef))).Expect<AnySharpObject>();
		var victimObj = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		await Assert.That(await permissionService.Controls(moverObj, victimObj)).IsTrue();
		await Assert.That(await permissionService.Controls(moverObj, zoneNode)).IsFalse();

		await Parser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		var moved = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		await Assert.That((await moved.Object().Zone.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
	}

	/// <summary>
	/// PennMUSH <c>check_zone_lock</c> (<c>src/lock.c:962</c>) installs <c>=me</c> on a zone that has
	/// none, through <c>add_lock(GOD, …)</c> — a system write, not the triggering player's. Going
	/// through <c>ILockService.SetAsync</c> instead would run the write against that player.
	/// <para>The lock is <c>Zone_Lock</c>, not <c>Chzone_Lock</c>: Chzone says who may zone an object
	/// <em>to</em> this one (<c>src/set.c:409</c>), while Zone is what hands control of a zoned object
	/// to whoever passes it (<c>src/predicat.c:409</c>). Writing the default onto Chzone left the
	/// control lock open and shut the destination gate against everyone but the zone itself.</para>
	/// </summary>
	[Test]
	public async ValueTask ChzoneInstallsTheDefaultZoneLockAsASystemWrite()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_DefaultLock");
		var zone = await CreateOwnedBy(owner, "DefaultLockZone");
		var victim = await CreateOwnedBy(owner, "DefaultLockVictim");

		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		var zoneNode = (await Mediator.Send(new GetObjectNodeQuery(zone))).Expect<AnySharpObject>();
		var installed = zoneNode.Object().Locks[nameof(LockType.Zone)];
		await Assert.That(installed.LockString).IsEqualTo($"=#{zone.Number}");
		await Assert.That(installed.Creator?.Number).IsEqualTo(1);

		// …and nothing was written to the Chzone lock, which would deny every later @chzone to this zone.
		await Assert.That(zoneNode.Object().Locks.ContainsKey(nameof(LockType.ChZone))).IsFalse();

		// lock.c:968-971 reports the install, naming the zone through unparse_object.
		var ownerNode = (await Mediator.Send(new GetObjectNodeQuery(owner.DbRef))).Expect<AnySharpObject>();
		var unparsed = await MessageFormatting.UnparseObjectAsync(
			WebAppFactoryArg.Services.GetRequiredService<IPermissionService>(), ownerNode, zoneNode,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>());
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(NotifyService,
			nameof(ErrorMessages.Notifications.ZoneAutomaticallyLockedFormat),
			$"Unlocked zone {unparsed} - automatically zone-locking to itself", owner.DbRef)).IsTrue();
	}

	/// <summary>
	/// <c>do_chzone</c> matches its object with <c>MAT_NEARBY</c> (<c>src/set.c:380</c>) — MAT_EVERYTHING
	/// plus MAT_NEAR — so an object that is neither near the player nor controlled by them is not one
	/// they can re-zone by naming its dbref. SharpMUSH matched with <c>MAT_EVERYTHING</c>, which has no
	/// such filter.
	/// </summary>
	[Test]
	public async ValueTask ChzoneWillNotReachAnObjectThatIsNotNearby()
	{
		var owner = await CreateTestPlayerWithHandleAsync("ZT_NearbyOwner");
		var stranger = await CreateTestPlayerWithHandleAsync("ZT_NearbyStranger");

		// In `owner`'s inventory, so `stranger` neither controls it nor stands anywhere near it.
		var victim = await CreateOwnedBy(owner, "NearbyVictim");
		var zone = await CreateOwnedBy(stranger, "NearbyZone");

		await Parser.CommandParse(stranger.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		// Pattern C: `stranger` is unique to this test, so Received(1) is unambiguous.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(stranger.DbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")),
				TestHelpers.MatchingObject(stranger.DbRef), INotifyService.NotificationType.Announce);

		var after = (await Mediator.Send(new GetObjectNodeQuery(victim))).Expect<AnySharpObject>();
		await Assert.That((await after.Object().Zone.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
	}

	/// <summary>
	/// <c>do_chzone</c>'s admin-owned warning (<c>src/set.c:452-456</c>), which fires on
	/// <c>Hasprivs(Owner(thing))</c> and so does not depend on the flags the object itself carries.
	/// </summary>
	[Test]
	public async ValueTask ChzoneWarnsAboutAnAdminOwnedObject()
	{
		var admin = await CreateWizardWithHandleAsync("ZT_AdminOwner");
		var zone = await CreateOwnedBy(admin, "AdminOwnedZone");
		var victim = await CreateOwnedBy(admin, "AdminOwnedVictim");

		await Parser.CommandParse(admin.Handle, ConnectionService, MarkupText.Plain($"@chzone {victim}={zone}"));

		// Pattern C: `admin` is unique to this test, so the key match is unambiguous.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.ChzoningAdminOwnedObject), admin.DbRef, admin.DbRef)).IsTrue();
	}

	/// <summary>
	/// <c>do_chzone</c>'s <c>else</c> branch (<c>src/set.c:477-482</c>): when the privileges survive —
	/// here because a wizard's <c>/preserve</c> kept them — the change says what was kept rather than
	/// passing in silence.
	/// </summary>
	[Test]
	public async ValueTask ChzonePreserveWarnsThatThePrivilegesWereKept()
	{
		var admin = await CreateWizardWithHandleAsync("ZT_KeptPrivilege");
		var zone = await CreateOwnedBy(admin, "KeptPrivilegeZone");
		var victim = await CreateOwnedBy(admin, "KeptPrivilegeVictim");

		await Parser.CommandParse(admin.Handle, ConnectionService, MarkupText.Plain($"@set {victim}=TRUST"));
		await Assert.That(await (await Node(victim)).HasFlag("TRUST")).IsTrue();

		await Parser.CommandParse(admin.Handle, ConnectionService,
			MarkupText.Plain($"@chzone/preserve {victim}={zone}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.ChzoningTrustPlayer), admin.DbRef, admin.DbRef)).IsTrue();
		// The whole point of /preserve: the flag the strip would have taken is still there.
		await Assert.That(await (await Node(victim)).HasFlag("TRUST")).IsTrue();
	}

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	/// <summary>
	/// A fresh player carrying WIZARD, so a test that needs an admin executor still gets a receiver
	/// unique to itself rather than sharing #1 with the rest of the session.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> CreateWizardWithHandleAsync(string namePrefix)
	{
		var player = await CreateTestPlayerWithHandleAsync(namePrefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set #{player.DbRef.Number}=WIZARD"));
		await Assert.That(await (await Node(player.DbRef)).IsWizard()).IsTrue();
		return player;
	}

	[Test]
	public async ValueTask ChzoneInvalidObject()
	{
		// Pattern C: the failed-locate notification is sent by many LocateService calls across the
		// session to executor #1. Use a unique receiver (fresh player) so Received(1) is sound.
		// @chzone matches via match_controlled -> noisy_match_result, so the wording is match.c:485's
		// "I can't see that here." — "I don't see that here." is look.c/move.c's string.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidObj");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@chzone #99999=#1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ChzoneInvalidZone()
	{
		// Pattern C: the failed-locate notification is sent by many LocateService calls to #1 across
		// the session. Use a fresh player as the unique executor so Received(1) is unambiguous.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidZone");

		// Create a unique object as the fresh player (they will own it → controls check passes)
		var objName = TestIsolationHelpers.GenerateUniqueName("InvalidZoneTest");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}=#99999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRExitMatchingTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRExitTest");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMR");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedRoom");
		var room1Result = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var room1DbRefText = room1Result.Message.ToPlainText()!;
		var room1Match = System.Text.RegularExpressions.Regex.Match(room1DbRefText, @"#(\d+)");
		if (!room1Match.Success) return;
		var room1DbRef = new DBRef(int.Parse(room1Match.Groups[1].Value));
		var room1Object = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		await Assert.That(room1Object.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {room1DbRef}={zmrDbRef}"));

		var zonedRoom = (await Mediator.Send(new GetObjectNodeQuery(room1DbRef))).Expect<AnySharpObject>();
		var roomZone = (await zonedRoom.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(roomZone.Object().DBRef.Number).IsEqualTo(zmrDbRef.Number);

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@open zmr_exit_{Random.Shared.Next(1000, 9999)}={room1DbRef},,{zmrDbRef}"));

		var zmrVerify = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrVerify.IsNone).IsFalse();
	}

	[Test, Skip("Failing")]
	public async ValueTask ZMRUserDefinedCommandTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRCmd");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRCmd");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedCmdRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		var zonedRoom = (await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef))).Expect<AnySharpObject>();
		var roomZone = await zonedRoom.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		var cmdObjName = TestIsolationHelpers.GenerateUniqueName("ZMRCmdObj");
		var cmdObjResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@create {cmdObjName}"));
		var cmdObjDbRef = DBRef.Parse(cmdObjResult.Message.ToPlainText()!);
		var cmdObject = await Mediator.Send(new GetObjectNodeQuery(cmdObjDbRef));
		await Assert.That(cmdObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {cmdObjDbRef}={zmrDbRef}"));

		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrtest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {cmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: ZMR command executed"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {zonedRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: ZMR command executed")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}

	[Test, Skip("Failed")]
	public async ValueTask PersonalZoneUserDefinedCommandTest()
	{
		// Use a fresh player so this test never mutates the shared player #1
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZT_PersonalZone");

		var personalZMRName = TestIsolationHelpers.GenerateUniqueName("PersonalZMR");
		var personalZMRResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@dig {personalZMRName}"));
		var personalZMRDbRefText = personalZMRResult.Message.ToPlainText();
		var personalZMRMatch = System.Text.RegularExpressions.Regex.Match(personalZMRDbRefText, @"#(\d+)");
		if (!personalZMRMatch.Success) return;
		var personalZMRDbRef = new DBRef(int.Parse(personalZMRMatch.Groups[1].Value));
		var personalZMRObject = await Mediator.Send(new GetObjectNodeQuery(personalZMRDbRef));
		await Assert.That(personalZMRObject.IsNone).IsFalse();

		// Set the TEST PLAYER'S zone to the ZMR (this is the "personal zone" concept)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone me={personalZMRDbRef}"));

		var playerObj = (await Mediator.Send(new GetObjectNodeQuery(testPlayer.DbRef))).Expect<AnySharpObject>();
		var playerZone = (await playerObj.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(playerZone.Object().DBRef.Number).IsEqualTo(personalZMRDbRef.Number);

		var personalCmdObjName = TestIsolationHelpers.GenerateUniqueName("PersonalCmdObj");
		var personalCmdObjResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {personalCmdObjName}"));
		var personalCmdObjDbRef = DBRef.Parse(personalCmdObjResult.Message.ToPlainText()!);
		var personalCmdObject = await Mediator.Send(new GetObjectNodeQuery(personalCmdObjDbRef));
		await Assert.That(personalCmdObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@tel {personalCmdObjDbRef}={personalZMRDbRef}"));

		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("personaltest");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {personalCmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Handle}={cmdName}: Personal zone command executed"));

		var testRoomName = TestIsolationHelpers.GenerateUniqueName("PersonalZoneTestRoom");
		var testRoomResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@dig {testRoomName}"));
		var testRoomDbRefText = testRoomResult.Message.ToPlainText();
		var testRoomMatch = System.Text.RegularExpressions.Regex.Match(testRoomDbRefText, @"#(\d+)");
		if (!testRoomMatch.Success) return;
		var testRoomDbRef = new DBRef(int.Parse(testRoomMatch.Groups[1].Value));
		var testRoomObject = await Mediator.Send(new GetObjectNodeQuery(testRoomDbRef));
		await Assert.That(testRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@tel {testRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: Personal zone command executed")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRDoesNotMatchCommandsOnZMRItself()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRSelfTest");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRSelfTest");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("SelfTestRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		var zonedRoom = (await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef))).Expect<AnySharpObject>();
		var roomZone = await zonedRoom.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		// Set a $-command directly on the ZMR itself with unique command name (should be ignored per spec).
		// Pattern A: embed the unique token into the @pemit body.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrselftest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {zmrDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: This should not execute"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {zonedRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the unique token in the message makes this a precise negative assertion.
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: This should not execute")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}
}
