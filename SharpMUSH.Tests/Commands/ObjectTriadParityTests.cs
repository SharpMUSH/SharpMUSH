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
/// back is preceded by <see cref="Settle"/>. That is Penn's ordering: the action is a separate queue
/// entry, not something the command runs before it returns.
/// </para>
/// </remarks>
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
		var value = await GodParser.EvaluateAsync(MarkupText.Plain($"[get({holder}/{attribute})]"));
		return value.ToPlainText().Trim();
	}

	private async Task<string> Location(DBRef what)
	{
		var value = await GodParser.FunctionParse(MarkupText.Plain($"[loc({what})]"));
		return value!.Message.ToPlainText().Trim();
	}

	/// <summary>Digs a fresh room and silently gathers every named object into it.</summary>
	private async Task<string> Room(string prefix, params object[] occupants)
	{
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = dig.Message.ToPlainText().Trim();

		foreach (var occupant in occupants)
		{
			await God($"@teleport/silent {occupant}={room}");
		}

		return room;
	}

	/// <summary>
	/// Creates a player who starts out in a fresh room, for a test that asserts every line that player
	/// hears. A player gathered in by <see cref="Room"/> starts in the default home, which every
	/// parallel test shares: a room message whose recipients were listed before the teleport still
	/// reaches the player after it.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> PlayerInRoomOfItsOwn(string prefix)
	{
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix + "Room")}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());

		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
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

	private Task Settle() => Scheduler.SettleForTestsAsync();

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
		await Settle();

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

		await Settle();
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
		await Settle();

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
		await Settle();

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

		await Settle();
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
		await Settle();

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
		await Settle();

		await Assert.That(await Read(room, "BOUNCED")).IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("the drop-in lock is failed on the room");
		await Assert.That(await Read(item, "DROPPED")).IsEqualTo($"#{dropper.DbRef.Number}")
			.Because("do_drop falls through the drop-in branch to its unconditional DROP triad");

		var location = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location.ToPlainText().Trim()))
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
		await Settle();

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
		await Settle();

		await Assert.That(await Read(room, "BOUNCED")).IsEmpty()
			.Because("the lock names the dropper, and the dropper is who it is evaluated against");

		var location = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location.ToPlainText().Trim()))
			.IsEqualTo(BareDbrefs(room))
			.Because("the drop-in lock passed, so the item reached the room");
	}

	// --- EMPTY ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>eval_lock_with(player, thing_loc, DropIn_Lock, pe_info)</c> (<c>src/move.c:829</c>):
	/// <c>empty me</c> evaluates the destination's drop-in lock against the PLAYER emptying their
	/// own inventory, not against each item being moved. It is the one branch of <c>do_empty</c>
	/// that consults the drop-in lock at all — the container branch (<c>move.c:836-853</c>) gates on
	/// the drop locks instead.
	/// </summary>
	[Test]
	public async ValueTask EmptyMeEvaluatesTheDropInLockAgainstThePlayer()
	{
		var emptier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EmptyActor");
		var item = await Thing("EmptyItem");
		var room = await Room("EmptyRoom", emptier.DbRef);

		await God($"@teleport/silent {item}={emptier.DbRef}");

		// A lock naming somebody else: the emptier fails it, so nothing leaves their hands.
		var stranger = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "EmptyStranger");
		await God($"@lock/dropin {room}=#{stranger.Number}");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain("empty me"));
		await Settle();

		var refused = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(refused.ToPlainText().Trim()))
			.IsEqualTo(BareDbrefs(emptier.DbRef.ToString()))
			.Because("the drop-in lock is evaluated against the emptier, and the emptier fails it");

		// The control: relocked to name the emptier, the same command moves the same item.
		await God($"@lock/dropin {room}=#{emptier.DbRef.Number}");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain("empty me"));
		await Settle();

		var location = await GodParser.FunctionParse(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(location!.Message.ToPlainText().Trim())).IsEqualTo(BareDbrefs(room))
			.Because("the drop-in lock names the emptier, who is who it is evaluated against");
	}

	/// <summary>
	/// <c>did_it_with(player, item, "SUCCESS", …, NOTHING, thing_loc, NOTHING, NA_INTER_HEAR)</c>
	/// (<c>src/move.c:874-876</c>): emptying a container you are not is the get half of
	/// <c>do_empty</c>, so each item fires its own SUCCESS triad with the CONTAINER'S LOCATION in
	/// <c>%0</c> — not the container — and the action attribute is queued on the item.
	/// </summary>
	/// <remarks>
	/// The container branch (<c>move.c:836-878</c>) is the half <c>empty me</c> never reaches: a
	/// different gate, a different lock set, and the triads fire per item rather than once.
	/// </remarks>
	[Test]
	public async ValueTask EmptyingAContainerFiresEachItemsSuccessTriadWithTheContainersLocationInEnvZero()
	{
		var emptier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EmptyBoxActor");
		var room = await Room("EmptyBoxRoom", emptier.DbRef);
		var box = await Thing("EmptyBoxContainer");
		var item = await Thing("EmptyBoxItem");

		await God($"@teleport/silent {emptier.DbRef}={room}");
		await God($"@teleport/silent {box}={room}");
		await God($"@teleport/silent {item}={box}");
		// move.c:836-838: the emptier neither owns nor controls a Thing()-made box, so ENTER_OK plus
		// an unset enter lock is the half of the gate that admits them.
		await God($"@set {box}=ENTER_OK");
		await God($"&SUCCESS {item}=You lift it clear.");
		await God($"&ASUCCESS {item}=&TOOK me=%0");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain($"empty {box}"));
		await Settle();

		// move.c:865-903 runs both halves: the get puts the item in the emptier's hands, and the
		// drop that follows (thing_loc != player) puts it down where the container stands.
		var landed = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(landed.ToPlainText().Trim())).IsEqualTo(BareDbrefs(room))
			.Because("the drop half leaves the item where the container stands, not in the emptier's hands");

		await Assert.That(BareDbrefs(await Read(item, "TOOK"))).IsEqualTo(BareDbrefs(room))
			.Because("env0 is the container's location, which is where do_get puts the source container");
	}

	/// <summary>
	/// <c>could_doit(player, item)</c> reported as <c>fail_lock(player, thing, Basic_Lock, NULL, …)</c>
	/// (<c>src/move.c:838-843</c>): the item's basic lock decides, and the refusal is attributed to
	/// the CONTAINER with no default message, so it is silent unless the container carries a
	/// <c>@failure</c> of its own.
	/// </summary>
	[Test]
	public async ValueTask EmptyingAContainerFailsTheItemsBasicLockOnTheContainer()
	{
		var emptier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EmptyLockActor");
		var room = await Room("EmptyLockRoom", emptier.DbRef);
		var box = await Thing("EmptyLockContainer");
		var item = await Thing("EmptyLockItem");

		await God($"@teleport/silent {emptier.DbRef}={room}");
		await God($"@teleport/silent {box}={room}");
		await God($"@teleport/silent {item}={box}");

		await God($"@set {box}=ENTER_OK");

		// The ITEM is locked against everyone; the FAILURE attribute lives on the CONTAINER.
		await God($"@lock {item}=#0");
		await God($"&AFAILURE {box}=&REFUSED me=yes");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain($"empty {box}"));
		await Settle();

		var stayed = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({item})]"));
		await Assert.That(BareDbrefs(stayed.ToPlainText().Trim())).IsEqualTo(BareDbrefs(box.ToString()))
			.Because("the item's basic lock refuses, so it does not leave the container");

		await Assert.That(await Read(box, "REFUSED")).IsEqualTo("yes")
			.Because("the refusal is attributed to the container, so the container's @afailure runs");
	}

	/// <summary>
	/// <c>do_empty</c> walks the container with <c>first_visible</c> (<c>src/move.c:820</c>,
	/// <c>src/predicat.c:292-304</c>): a DARK item the emptier neither controls nor sees for any other
	/// reason is skipped and stays where it is, while its visible neighbour is emptied.
	/// </summary>
	[Test]
	public async ValueTask EmptyingAContainerSkipsADarkItemTheEmptierCannotSee()
	{
		var emptier = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EmptyDarkActor");
		var room = await Room("EmptyDarkRoom", emptier.DbRef);
		var box = await Thing("EmptyDarkContainer");
		var shown = await Thing("EmptyDarkShown");
		var hidden = await Thing("EmptyDarkHidden");

		await God($"@teleport/silent {box}={room}");
		await God($"@teleport/silent {shown}={box}");
		await God($"@teleport/silent {hidden}={box}");
		await God($"@set {box}=ENTER_OK");
		await God($"@set {hidden}=DARK");

		await GodParser.CommandParse(emptier.Handle, ConnectionService, MarkupText.Plain($"empty {box}"));
		await Settle();

		var hiddenAt = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({hidden})]"));
		await Assert.That(BareDbrefs(hiddenAt.ToPlainText().Trim())).IsEqualTo(BareDbrefs(box.ToString()))
			.Because("first_visible skips a DARK item the emptier does not control");

		var shownAt = await GodParser.EvaluateAsync(MarkupText.Plain($"[loc({shown})]"));
		await Assert.That(BareDbrefs(shownAt.ToPlainText().Trim())).IsEqualTo(BareDbrefs(room))
			.Because("the visible item beside it is still emptied");
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
		await Settle();

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
		await Settle();

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
		await Settle();

		await Assert.That(await Read(gift, "KEPT")).IsEqualTo($"#{giver.DbRef.Number}");
	}

	/// <summary>
	/// <c>match_result(player, recipient, TYPE_PLAYER, MAT_NEAR_THINGS | MAT_ENGLISH)</c>
	/// (<c>src/rob.c:283-284</c>) carries <c>MAT_NEAR</c>, and a player resolved by <c>*&lt;name&gt;</c>
	/// is dropped again unless <c>nearby || controls</c> (<c>src/match.c:398-399</c>). So
	/// <c>give *&lt;someone elsewhere&gt;=1</c> finds nobody and says so — it never reaches the amount,
	/// which is why PennMUSH answers this with <c>Give to whom?</c> and not with anything about money.
	/// </summary>
	[Test]
	public async ValueTask GiveToAPlayerInAnotherRoomFindsNobody()
	{
		var giver = await PlayerInRoomOfItsOwn("GiveFarGiver");
		var elsewhere = await PlayerInRoomOfItsOwn("GiveFarTarget");

		var seen = await MessagesWhile(giver.DbRef, async () =>
			await GodParser.CommandParse(giver.Handle, ConnectionService,
				MarkupText.Plain($"give *{elsewhere.Name}=1")));

		await Assert.That(seen).IsEquivalentTo(new[] { "Give to whom?" })
			.Because("MAT_NEAR drops the player, and rob.c:287 is the whole of the reply");
	}

	/// <summary>
	/// The other arm of the same <c>NOTHING</c> case (<c>src/rob.c:286-288</c>): a name no player
	/// answers to. One line, not the notifying locate's "I can't see that here." followed by a second.
	/// </summary>
	[Test]
	public async ValueTask GiveToANameNobodyAnswersToFindsNobody()
	{
		var giver = await PlayerInRoomOfItsOwn("GiveMissGiver");
		var absent = TestIsolationHelpers.GenerateUniqueName("GiveMissNobody");

		var seen = await MessagesWhile(giver.DbRef, async () =>
			await GodParser.CommandParse(giver.Handle, ConnectionService, MarkupText.Plain($"give *{absent}=1")));

		await Assert.That(seen).IsEquivalentTo(new[] { "Give to whom?" })
			.Because("rob.c answers a failed recipient match with exactly one line");
	}

	/// <summary>
	/// <c>MAT_PLAYER</c> is still in the set, so the <c>*&lt;name&gt;</c> form works on a player who is
	/// standing there: the near check is <c>nearby(who, match) || controls(…)</c>, not a removal of the
	/// player scope. Without this, narrowing the flags would have looked like a fix while breaking
	/// every <c>give *someone=&lt;gift&gt;</c> in the room.
	/// </summary>
	[Test]
	public async ValueTask GiveToAPlayerInTheRoomStillResolvesTheStarForm()
	{
		var giver = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GiveNearGiver");
		var nearby = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GiveNearTarget");
		var gift = await Thing("GiveNearGift");
		await Room("GiveNearRoom", giver.DbRef, nearby.DbRef, gift);

		await God($"@set {nearby.DbRef}=ENTER_OK");
		await GodParser.CommandParse(giver.Handle, ConnectionService, MarkupText.Plain($"get {gift}"));

		await GodParser.CommandParse(giver.Handle, ConnectionService,
			MarkupText.Plain($"give *{nearby.Name}={gift}"));

		await Assert.That(BareDbrefs(await Location(gift))).IsEqualTo($"#{nearby.DbRef.Number}")
			.Because("a nearby player passes MAT_NEAR, so the gift changes hands");
	}

	/// <summary>
	/// <c>match_result(player, amnt, TYPE_THING, MAT_POSSESSION | MAT_ENGLISH)</c>'s <c>NOTHING</c>
	/// (<c>src/rob.c:302-305</c>): one line, <c>You don't have that!</c>. The notifying locate put
	/// "I can't see that here." in front of it.
	/// </summary>
	[Test]
	public async ValueTask GiveOfSomethingTheGiverDoesNotHaveSaysSoOnce()
	{
		var giver = await PlayerInRoomOfItsOwn("GiveNoGiftGiver");
		var recipient = await Thing("GiveNoGiftBox");
		await God($"@teleport/silent {recipient}={await Location(giver.DbRef)}");
		var absent = TestIsolationHelpers.GenerateUniqueName("GiveNoGiftAbsent");

		var seen = await MessagesWhile(giver.DbRef, async () =>
			await GodParser.CommandParse(giver.Handle, ConnectionService,
				MarkupText.Plain($"give {recipient}={absent}")));

		await Assert.That(seen).IsEquivalentTo(new[] { "You don't have that!" })
			.Because("rob.c:303 is the whole of the reply to a gift that matched nothing");
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

		await Settle();
		await Assert.That(await Read(gadget, "USED")).IsEqualTo($"#{user.DbRef.Number}");
	}

	/// <summary>
	/// <c>charge_action</c> (<c>src/predicat.c:88-114</c>), called from <c>do_use</c>
	/// (<c>src/set.c:1416-1417</c>): an object with CHARGES left spends one and runs AUSE; at zero it
	/// runs RUNOUT instead and the count stays at zero.
	/// </summary>
	[Test]
	public async ValueTask UseSpendsAChargeAndRunsRunoutOnceTheyAreGone()
	{
		var user = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UseChargeUser");
		var gadget = await Thing("UseChargeGadget");
		await Room("UseChargeRoom", user.DbRef, gadget);

		await God($"&CHARGES {gadget}=1");
		await God($"&AUSE {gadget}=&USES me=[add(0[get(me/USES)],1)]");
		await God($"&RUNOUT {gadget}=&RANOUT me=[add(0[get(me/RANOUT)],1)]");

		await GodParser.CommandParse(user.Handle, ConnectionService, MarkupText.Plain($"use {gadget}"));
		await Settle();

		await Assert.That(await Read(gadget, "CHARGES")).IsEqualTo("0");
		await Assert.That(await Read(gadget, "USES")).IsEqualTo("1");
		await Assert.That(await Read(gadget, "RANOUT")).IsEqualTo(string.Empty);

		await GodParser.CommandParse(user.Handle, ConnectionService, MarkupText.Plain($"use {gadget}"));
		await Settle();

		await Assert.That(await Read(gadget, "CHARGES")).IsEqualTo("0");
		await Assert.That(await Read(gadget, "USES")).IsEqualTo("1")
			.Because("with no charges left AUSE does not run");
		await Assert.That(await Read(gadget, "RANOUT")).IsEqualTo("1");
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

		await Settle();
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

		await Settle();
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
			.Message.ToPlainText().Trim();
		var newName = TestIsolationHelpers.GenerateUniqueName("NameTriadNew");

		// The renamer has to control the sign for @name to run at all; WIZARD is the smallest way
		// to get there without @chown, which sets HALT and would suppress ANAME. The factory is
		// SharedType.PerTestSession, so the flag has to come back off however this test ends —
		// otherwise every later test in the run sees this player as a wizard.
		await God($"@set {renamer.DbRef}=WIZARD");

		try
		{
			await God($"&ONAME {sign}=repaints the sign.");
			await God($"&ANAME {sign}=&RENAMED me=%0/%1");

			var seen = await MessagesWhile(watcher.DbRef, async () =>
				await GodParser.CommandParse(renamer.Handle, ConnectionService,
					MarkupText.Plain($"@name {sign}={newName}")));

			await Assert.That(seen.Any(m => m == $"{renamer.Name} repaints the sign.")).IsTrue()
						.Because("ONAME is name-prefixed and shown to the renamer's room");

			await Settle();
			await Assert.That(await Read(sign, "RENAMED")).IsEqualTo($"{oldName}/{newName}");
		}
		finally
		{
			await God($"@set {renamer.DbRef}=!WIZARD");
		}
	}
}
