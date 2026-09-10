using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>GET</c>, <c>DROP</c>, <c>GIVE</c>, <c>USE</c>, <c>PAGE</c> and <c>@name</c> fire the triads
/// PennMUSH fires, on the objects PennMUSH fires them on.
/// </summary>
/// <remarks>
/// <para>
/// Action attributes go through <c>queue_attribute_base</c> now, so every assertion that reads one
/// back is preceded by a drain. That is Penn's ordering: the action is a separate queue entry, not
/// something the command runs before it returns.
/// </para>
/// <para>
/// <c>[NotInParallel]</c> for the same reason <see cref="Services.DidItServiceTests"/> carries it —
/// <c>DrainImmediateQueueForTests</c> waits on the whole session-shared immediate queue with a
/// throwing timeout.
/// </para>
/// </remarks>
[NotInParallel]
public class ObjectTriadParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);

	private async Task God(string command)
		=> await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> Read(DBRef holder, string attribute)
		=> await Read(holder.ToString(), attribute);

	private async Task<string> Read(string holder, string attribute)
	{
		var value = await GodParser.FunctionParse(MarkupText.Plain($"[get({holder}/{attribute})]"));
		return value!.Message!.ToPlainText().Trim();
	}

	/// <summary>Digs a fresh room and silently gathers every named object into it.</summary>
	private async Task<string> Room(string prefix, params object[] occupants)
	{
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = dig.Message!.ToPlainText().Trim();

		foreach (var occupant in occupants)
		{
			await God($"@teleport/silent {occupant}={room}");
		}

		return room;
	}

	/// <summary>
	/// Strips the creation stamp off every reference in a slash-separated list. A triad's %0/%1
	/// carry whatever <c>DBRef.ToString()</c> produced, which is a full objid when the reference
	/// was resolved from a live object — the same reason
	/// <see cref="MovementParityTests"/> keeps its own bare-dbref helper.
	/// </summary>
	private static string BareDbrefs(string value)
		=> string.Join("/", value.Split('/').Select(part =>
		{
			var colon = part.IndexOf(':');
			return colon < 0 ? part : part[..colon];
		}));

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	// --- GET ------------------------------------------------------------------------------------

	/// <summary>
	/// <c>did_it_with(player, thing, "SUCCESS", …, "ASUCCESS", NOTHING, oldloc, …)</c>
	/// (<c>src/move.c:685-686</c>).
	/// </summary>
	[Test]
	public async ValueTask GetFiresTheItemsSuccessTriad()
	{
		var taker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GetTriadTaker");
		var item = await Thing("GetTriadItem");
		await Room("GetTriadRoom", taker.DbRef, item);

		await God($"&ASUCCESS {item}=&TAKEN me=%#");

		await GodParser.CommandParse(taker.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(item, "TAKEN")).IsEqualTo($"#{taker.DbRef.Number}")
			.Because("ASUCCESS runs on the item with the taker as enactor");
	}

	/// <summary>
	/// <c>did_it_with(player, player, "RECEIVE", NULL, "ORECEIVE", NULL, "ARECEIVE", NOTHING, thing,
	/// NOTHING, NA_INTER_HEAR, AN_MOVE)</c> (<c>src/move.c:687-689</c>). The eighth argument is
	/// <c>loc</c> and the ninth is <c>env0</c>, so the o-message goes to the taker's room — which
	/// <c>NOTHING</c> resolves to (<c>src/predicat.c:230</c>) — and the taken item rides in
	/// <c>%0</c>.
	/// </summary>
	[Test]
	public async ValueTask GetsReceiveOMessageReachesTheRoomAndCarriesTheItemInEnv0()
	{
		var taker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ReceiveTriadTaker");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ReceiveTriadWatch");
		var item = await Thing("ReceiveTriadItem");
		await Room("ReceiveTriadRoom", taker.DbRef, watcher.DbRef, item);

		await God($"&ORECEIVE {taker.DbRef}=pockets it.");
		await God($"&ARECEIVE {taker.DbRef}=&POCKETED me=%0");

		var seen = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(taker.Handle, ConnectionService, MarkupText.Plain($"get {item}")));

		await Assert.That(seen.Any(m => m == $"{taker.Name} pockets it.")).IsTrue()
			.Because("loc is NOTHING, so ORECEIVE is broadcast into the taker's room");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(BareDbrefs(await Read(taker.DbRef, "POCKETED"))).IsEqualTo($"#{item.Number}")
			.Because("env0 is the taken object");
	}

	/// <summary>
	/// The take lock is evaluated and failed against the object's own location, not the item:
	/// <c>eval_lock_with(player, oldloc, Take_Lock, …)</c> then
	/// <c>fail_lock(player, oldloc, Take_Lock, …)</c> (<c>src/move.c:668-673</c>).
	/// </summary>
	[Test]
	public async ValueTask GetsTakeLockFailsOnTheSourceRoomNotTheItem()
	{
		var taker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TakeLockTaker");
		var item = await Thing("TakeLockItem");
		var room = await Room("TakeLockRoom", taker.DbRef, item);

		await God($"@lock/take {room}=#0");
		await God($"&TAKE_LOCK`AFAILURE {room}=&REFUSED me=%#");
		// The same attributes on the item must stay untouched: fail_lock names oldloc, not thing.
		await God($"&TAKE_LOCK`AFAILURE {item}=&REFUSED me=wrong-object");

		await GodParser.CommandParse(taker.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(room, "REFUSED")).IsEqualTo($"#{taker.DbRef.Number}")
			.Because("fail_lock names oldloc, so the source room's take-failure action runs");
		await Assert.That(await Read(item, "REFUSED")).IsEmpty()
			.Because("the item is not the object the take lock was failed on");
	}

	/// <summary>
	/// The possessive path folds both locks into one condition with a single <c>else</c>, so every
	/// refusal reports as <c>fail_lock(player, thing, Basic_Lock, T("You can't take that from
	/// there."), NOTHING)</c> (<c>src/move.c:635-642</c>): the FAILURE family on the ITEM, carrying
	/// the take lock's text.
	/// </summary>
	[Test]
	public async ValueTask PossessiveGetFailsTheBasicLockOnTheItem()
	{
		var taker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PossessTaker");
		var box = await Thing("PossessBox");
		var item = await Thing("PossessItem");
		await Room("PossessRoom", taker.DbRef, box);

		await God($"@set {box}=ENTER_OK");
		await God($"@teleport/silent {item}={box}");
		await God($"@lock/take {box}=#0");
		await God($"&AFAILURE {item}=&REFUSED me=%#");
		await God($"&TAKE_LOCK`AFAILURE {box}=&REFUSED me=wrong-object");

		await GodParser.CommandParse(taker.Handle, ConnectionService,
			MarkupText.Plain($"get {box}'s {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(item, "REFUSED")).IsEqualTo($"#{taker.DbRef.Number}")
			.Because("the possessive else fires Basic_Lock's failure family on the item");
		await Assert.That(await Read(box, "REFUSED")).IsEmpty()
			.Because("the container's take-failure family is not what the possessive else names");
	}

	// --- DROP -----------------------------------------------------------------------------------

	/// <summary>
	/// <c>did_it(player, thing, "DROP", …, "ODROP", …, "ADROP", NOTHING)</c>
	/// (<c>src/move.c:768-769</c>), and the o-message carries the dropper's name in front
	/// (<c>UFUN_NAME</c>, <c>src/utils.c:351</c>).
	/// </summary>
	[Test]
	public async ValueTask DropFiresTheItemsDropTriad()
	{
		var dropper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropTriadActor");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropTriadWatch");
		var item = await Thing("DropTriadItem");
		await Room("DropTriadRoom", dropper.DbRef, watcher.DbRef, item);

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await God($"&ODROP {item}=lets go of it.");
		await God($"&ADROP {item}=&DROPPED me=%#");

		var seen = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"drop {item}")));

		await Assert.That(seen.Any(m => m == $"{dropper.Name} lets go of it.")).IsTrue()
			.Because("ODROP is name-prefixed and shown to the rest of the room");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Read(item, "DROPPED")).IsEqualTo($"#{dropper.DbRef.Number}");
	}

	/// <summary>
	/// <c>fail_lock(player, thing, Drop_Lock, T("You can't seem to get rid of that."), NOTHING)</c>
	/// (<c>src/move.c:736-738</c>): the drop lock fails on the object being dropped.
	/// </summary>
	[Test]
	public async ValueTask DropsDropLockFailsOnTheObject()
	{
		var dropper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropLockActor");
		var item = await Thing("DropLockItem");
		await Room("DropLockRoom", dropper.DbRef, item);

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await God($"@lock/drop {item}=#0");
		await God($"&DROP_LOCK`AFAILURE {item}=&STUCK me=%#");

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"drop {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(item, "STUCK")).IsEqualTo($"#{dropper.DbRef.Number}");
	}

	/// <summary>
	/// The drop-in branch is the one <c>else if</c> in <c>do_drop</c>'s chain with no <c>return</c>
	/// (<c>src/move.c:745-747</c>): the object moves nowhere, but control still reaches the DROP
	/// triad at <c>src/move.c:768</c>.
	/// </summary>
	[Test]
	public async ValueTask DropInRefusalStillFiresTheDropTriad()
	{
		var dropper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropInActor");
		var item = await Thing("DropInItem");
		var room = await Room("DropInRoom", dropper.DbRef, item);

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await God($"@lock/dropin {room}=#0");
		await God($"&DROPIN_LOCK`AFAILURE {room}=&BOUNCED me=%#");
		await God($"&ADROP {item}=&DROPPED me=%#");

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"drop {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(room, "BOUNCED")).IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("the drop-in lock is failed on the room");
		await Assert.That(await Read(item, "DROPPED")).IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("do_drop falls through the drop-in branch to its unconditional DROP triad");

		var location = await GodParser.FunctionParse(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location!.Message!.ToPlainText().Trim()))
			.IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("the refused drop moves the object nowhere");
	}

	/// <summary>
	/// <c>fail_lock(player, loc, Drop_Lock, T("You can't seem to drop things here."), NOTHING)</c>
	/// (<c>src/move.c:740-744</c>): the drop lock is checked on the ROOM as well as on the thing,
	/// and that branch returns.
	/// </summary>
	[Test]
	public async ValueTask DropsDropLockIsAlsoCheckedOnTheRoom()
	{
		var dropper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RoomDropLockActor");
		var item = await Thing("RoomDropLockItem");
		var room = await Room("RoomDropLockRoom", dropper.DbRef, item);

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await God($"@lock/drop {room}=#0");
		await God($"&DROP_LOCK`AFAILURE {room}=&BARRED me=%#");
		await God($"&ADROP {item}=&DROPPED me=%#");

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"drop {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(room, "BARRED")).IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("the room's own drop lock is evaluated after the thing's");
		await Assert.That(await Read(item, "DROPPED")).IsEmpty()
			.Because("the room's drop-lock branch returns before the DROP triad");
	}

	/// <summary>
	/// <c>eval_lock_with(player, loc, DropIn_Lock, pe_info)</c> (<c>src/move.c:745</c>): the drop-in
	/// lock is evaluated against the DROPPER, not against the object being dropped, so a
	/// <c>@lock/dropin</c> keyed on a player matches when that player drops something.
	/// </summary>
	[Test]
	public async ValueTask DropInLockIsEvaluatedAgainstTheDropper()
	{
		var dropper = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DropInActorLock");
		var item = await Thing("DropInActorItem");
		var room = await Room("DropInActorRoom", dropper.DbRef, item);

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"get {item}"));
		await God($"@lock/dropin {room}=#{dropper.DbRef.Number}");
		await God($"&DROPIN_LOCK`AFAILURE {room}=&BOUNCED me=%#");

		await GodParser.CommandParse(dropper.Handle, ConnectionService, MarkupText.Plain($"drop {item}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(room, "BOUNCED")).IsEmpty()
			.Because("the lock names the dropper, and the dropper is who it is evaluated against");

		var location = await GodParser.FunctionParse(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location!.Message!.ToPlainText().Trim()))
			.IsEqualTo(BareDbrefs(room))
			.Because("the drop-in lock passed, so the item reached the room");
	}

	// --- EMPTY ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>eval_lock_with(player, thing_loc, DropIn_Lock, pe_info)</c> (<c>src/move.c:829</c>):
	/// <c>@empty</c> evaluates the destination's drop-in lock against the PLAYER emptying the
	/// container, not against each item being moved.
	/// </summary>
	[Test]
	public async ValueTask EmptyEvaluatesTheDropInLockAgainstThePlayer()
	{
		var emptier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EmptyActor");
		var box = await Thing("EmptyBox");
		var item = await Thing("EmptyItem");
		var room = await Room("EmptyRoom", emptier.DbRef, box);

		await God($"@set {box}=ENTER_OK");
		await God($"@teleport/silent {item}={box}");
		await God($"@lock/dropin {room}=#{emptier.DbRef.Number}");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain($"empty {box}"));
		await Scheduler.DrainImmediateQueueForTests();

		var location = await GodParser.FunctionParse(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location!.Message!.ToPlainText().Trim())).IsEqualTo(BareDbrefs(room))
			.Because("the drop-in lock names the emptier, who is who it is evaluated against");
	}

	// --- GIVE -----------------------------------------------------------------------------------

	/// <summary>
	/// <c>did_it_with(player, player, "GIVE", …, "AGIVE", NOTHING, thing, who, …)</c>
	/// (<c>src/rob.c:357-358</c>): GIVE/OGIVE/AGIVE live on the GIVER, with the gift in
	/// <c>%0</c> and the recipient in <c>%1</c>.
	/// </summary>
	[Test]
	public async ValueTask GiveFiresTheGiversGiveTriadWithGiftAndRecipientInTheEnvironment()
	{
		var giver = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GiveTriadGiver");
		var gift = await Thing("GiveTriadGift");
		var recipient = await Thing("GiveTriadBox");
		await Room("GiveTriadRoom", giver.DbRef, gift, recipient);

		await God($"@set {recipient}=ENTER_OK");
		await GodParser.CommandParse(giver.Handle, ConnectionService, MarkupText.Plain($"get {gift}"));
		await God($"&AGIVE {giver.DbRef}=&GAVE me=%0/%1");

		await GodParser.CommandParse(giver.Handle, ConnectionService,
			MarkupText.Plain($"give {recipient}={gift}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(BareDbrefs(await Read(giver.DbRef, "GAVE")))
			.IsEqualTo($"#{gift.Number}/#{recipient.Number}")
			.Because("AGIVE is the giver's attribute, and did_it_with binds %0=thing %1=who");
	}

	/// <summary>
	/// <c>did_it_with(who, who, "RECEIVE", …, "ARECEIVE", NOTHING, thing, player, …)</c>
	/// (<c>src/rob.c:369-370</c>) and <c>did_it(who, thing, "SUCCESS", …)</c> (<c>:364-365</c>):
	/// the recipient's receive triad and the gift's own success triad both run with the RECIPIENT
	/// as the enactor.
	/// </summary>
	[Test]
	public async ValueTask GiveFiresTheRecipientsReceiveTriadAndTheGiftsSuccessTriad()
	{
		var giver = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RecvTriadGiver");
		var gift = await Thing("RecvTriadGift");
		var recipient = await Thing("RecvTriadBox");
		await Room("RecvTriadRoom", giver.DbRef, gift, recipient);

		await God($"@set {recipient}=ENTER_OK");
		await GodParser.CommandParse(giver.Handle, ConnectionService, MarkupText.Plain($"get {gift}"));
		await God($"&ARECEIVE {recipient}=&GOT me=%0/%1");
		await God($"&ASUCCESS {gift}=&ACCEPTED me=%#");

		await GodParser.CommandParse(giver.Handle, ConnectionService,
			MarkupText.Plain($"give {recipient}={gift}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(BareDbrefs(await Read(recipient, "GOT")))
			.IsEqualTo($"#{gift.Number}/#{giver.DbRef.Number}")
			.Because("ARECEIVE binds %0=thing %1=player");
		await Assert.That(await Read(gift, "ACCEPTED")).IsEqualTo($"#{recipient.Number}")
			.Because("did_it(who, thing, …) makes the RECIPIENT the gift's enactor");
	}

	/// <summary>
	/// <c>fail_lock(player, thing, Give_Lock, T("You can't give that away."), NOTHING)</c>
	/// (<c>src/rob.c:325-327</c>): the give lock fails on the gift.
	/// </summary>
	[Test]
	public async ValueTask GivesGiveLockFailsOnTheGift()
	{
		var giver = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GiveLockGiver");
		var gift = await Thing("GiveLockGift");
		var recipient = await Thing("GiveLockBox");
		await Room("GiveLockRoom", giver.DbRef, gift, recipient);

		await God($"@set {recipient}=ENTER_OK");
		await GodParser.CommandParse(giver.Handle, ConnectionService, MarkupText.Plain($"get {gift}"));
		await God($"@lock/give {gift}=#0");
		await God($"&GIVE_LOCK`AFAILURE {gift}=&KEPT me=%#");

		await GodParser.CommandParse(giver.Handle, ConnectionService,
			MarkupText.Plain($"give {recipient}={gift}"));
		await Scheduler.DrainImmediateQueueForTests();

		await Assert.That(await Read(gift, "KEPT")).IsEqualTo($"#{giver.DbRef.Number}");
	}

	// --- USE ------------------------------------------------------------------------------------

	/// <summary>
	/// <c>did_it(player, thing, "USE", T("Used."), "OUSE", NULL, "AUSE", NOTHING, AN_SYS)</c>
	/// (<c>src/set.c:1416-1417</c>).
	/// </summary>
	[Test]
	public async ValueTask UseFiresTheUseTriadAndDefaultsToUsed()
	{
		var user = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UseTriadUser");
		var gadget = await Thing("UseTriadGadget");
		await Room("UseTriadRoom", user.DbRef, gadget);

		await God($"&AUSE {gadget}=&USED me=%#");

		var seen = await MessagesWhile(user.DbRef, async () =>
			await GodParser.CommandParse(user.Handle, ConnectionService, MarkupText.Plain($"use {gadget}")));

		await Assert.That(seen).Contains("Used.")
			.Because("set.c:1416 passes T(\"Used.\") as the USE default");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Read(gadget, "USED")).IsEqualTo($"#{user.DbRef.Number}");
	}

	/// <summary>
	/// <c>fail_lock(player, thing, Use_Lock, T("Permission denied."), NOTHING)</c>
	/// (<c>src/set.c:1413</c>), whose attributes come from <c>lock_msgs</c> as
	/// UFAIL/OUFAIL/AUFAIL (<c>src/lock.c:102-106</c>).
	/// </summary>
	[Test]
	public async ValueTask UsesUseLockFailsThroughUfail()
	{
		var user = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UfailUser");
		var gadget = await Thing("UfailGadget");
		await Room("UfailRoom", user.DbRef, gadget);

		await God($"@lock/use {gadget}=#0");
		await God($"&UFAIL {gadget}=It refuses [name(%#)].");
		await God($"&AUFAIL {gadget}=&BLOCKED me=%#");

		var seen = await MessagesWhile(user.DbRef, async () =>
			await GodParser.CommandParse(user.Handle, ConnectionService, MarkupText.Plain($"use {gadget}")));

		await Assert.That(seen.Any(m => m == $"It refuses {user.Name}.")).IsTrue()
			.Because("UFAIL is evaluated as the object with the actor as %#");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Read(gadget, "BLOCKED")).IsEqualTo($"#{user.DbRef.Number}");
	}

	// --- PAGE -----------------------------------------------------------------------------------

	/// <summary>
	/// <c>fail_lock(executor, target, Page_Lock, NULL, NOTHING)</c> (<c>src/speech.c:948</c>). The
	/// page lock is not in <c>lock_msgs</c>, so its attributes are the derived
	/// <c>PAGE_LOCK`FAILURE</c> family (<c>src/lock.c:861-870</c>), and they are evaluated as the
	/// recipient with the pager as <c>%#</c>.
	/// </summary>
	[Test]
	public async ValueTask PageLockFailureEvaluatesTheDerivedAttributes()
	{
		var pager = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PageLockPager");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PageLockTarget");

		await God($"@lock/page {target.DbRef}=#0");
		await God($"&PAGE_LOCK`FAILURE {target.DbRef}=No pages from [name(%#)].");
		await God($"&PAGE_LOCK`AFAILURE {target.DbRef}=&BOUNCED me=%#");

		var seen = await MessagesWhile(pager.DbRef, async () =>
			await GodParser.CommandParse(pager.Handle, ConnectionService,
				MarkupText.Plain($"page {target.Name}=hello")));

		await Assert.That(seen.Any(m => m == $"No pages from {pager.Name}.")).IsTrue()
			.Because("the derived failure attribute is evaluated, not echoed raw");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Read(target.DbRef, "BOUNCED")).IsEqualTo($"#{pager.DbRef.Number}");
	}

	// --- @name ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>real_did_it(player, thing, NULL, NULL, "ONAME", NULL, "ANAME", NOTHING, pe_regs,
	/// NA_INTER_PRESENCE, AN_SYS)</c> with <c>%0</c> the old name and <c>%1</c> the new
	/// (<c>src/set.c:155-158</c>).
	/// </summary>
	[Test]
	public async ValueTask RenameFiresTheNameTriadWithOldAndNewNames()
	{
		var renamer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NameTriadActor");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NameTriadWatch");
		var sign = await Thing("NameTriadSign");
		await Room("NameTriadRoom", renamer.DbRef, watcher.DbRef, sign);

		var oldName = (await GodParser.FunctionParse(MarkupText.Plain($"[name({sign})]")))!
			.Message!.ToPlainText().Trim();
		var newName = TestIsolationHelpers.GenerateUniqueName("NameTriadNew");

		// The renamer has to control the sign for @name to run at all; WIZARD is the smallest way
		// to get there without @chown, which sets HALT and would suppress ANAME.
		await God($"@set {renamer.DbRef}=WIZARD");
		await God($"&ONAME {sign}=repaints the sign.");
		await God($"&ANAME {sign}=&RENAMED me=%0/%1");

		var seen = await MessagesWhile(watcher.DbRef, async () =>
			await GodParser.CommandParse(renamer.Handle, ConnectionService,
				MarkupText.Plain($"@name {sign}={newName}")));

		await Assert.That(seen.Any(m => m == $"{renamer.Name} repaints the sign.")).IsTrue()
					.Because("ONAME is name-prefixed and shown to the renamer's room");

		await Scheduler.DrainImmediateQueueForTests();
		await Assert.That(await Read(sign, "RENAMED")).IsEqualTo($"{oldName}/{newName}");
	}
}
