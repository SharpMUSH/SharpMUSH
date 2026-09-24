using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What PennMUSH's <c>clone_object</c> (<c>src/create.c:614-668</c>) and <c>do_clone</c>
/// (<c>src/create.c:679-812</c>) carry from the original that SharpMUSH's two clone bodies did not:
/// the home, the parent, the zone, an exit's destination, the modification time, the queued
/// <c>ACLONE</c>, the <c>OBJECT`CREATE</c> event, and <c>/PRESERVE</c>'s wizard gate together with
/// the powers and warnings it exists to carry.
/// <para><c>fun_clone</c> is a call to <c>do_clone</c> (<c>src/fundb.c</c>), so every case here is
/// asserted of the command and of the function.</para>
/// </summary>
public class CloneStateParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeStore Attributes => WebAppFactoryArg.Services.GetRequiredService<IAttributeStore>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText()
			?? string.Empty;

	private Task<string> AsGod(string command) => Run(1, command);

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private static DBRef Ref(string reported) => DBRef.Parse(reported.Trim());

	private async Task<DBRef> Create(string name) => Ref(await AsGod($"@create {name}"));

	/// <summary>Where an exit leads — SharpMUSH's <c>Home</c>, PennMUSH's <c>Location</c>.</summary>
	private async Task<int?> DestinationOf(DBRef exit)
		=> AnyOptionalSharpContainer.RefOf(
			await (await Node(exit)).Expect<SharpExit>().Home.WithCancellation(CancellationToken.None))?.Number;

	/// <summary>How many objects currently answer to <paramref name="name"/>.</summary>
	private async Task<string[]> Named(string name)
		=> (await AsGod($"think lsearch(all,name,{name})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
	private async Task<DBRef> Dig(string name) => Ref(await AsGod($"@dig {name}"));

	/// <summary>The clone request, phrased for the command and for the function.</summary>
	private async Task<DBRef> Clone(bool throughTheFunction, DBRef target, string newName)
		=> Ref(throughTheFunction
			? await AsGod($"think clone({target},{newName})")
			: await AsGod($"@clone {target}={newName}"));

	private async Task<string> GetAsync(DBRef obj, string attr)
		=> (await Parser.FunctionParse(MarkupText.Plain($"get({obj}/{attr})")))?.Message?.ToPlainText() ?? string.Empty;

	/// <summary>Polls until a queued command list has written the attribute it ends with.</summary>
	private async Task WaitForAsync(DBRef obj, string attr, string expected)
		=> await Assert.That(async () => await GetAsync(obj, attr))
			.WaitsFor(value => value.IsEqualTo(expected), timeout: TimeSpan.FromSeconds(5),
				pollingInterval: TimeSpan.FromMilliseconds(50));

	/// <summary>
	/// Queued after the action under test, so once the marker has run the action has had its turn and
	/// an attribute it did not write is an action that did not run.
	/// </summary>
	private async Task DrainAsync(string uid)
	{
		var marker = await Create($"CspDrain{uid}");
		await AsGod($"&GO {marker}=&DONE me=1");
		await AsGod($"@trigger {marker}/GO");
		await WaitForAsync(marker, "DONE", "1");
	}

	/// <summary>
	/// <c>Home(clone) = Home(thing)</c> (create.c:657). SharpMUSH built every clone at the configured
	/// default home instead, so a cloned object went somewhere else entirely when sent <c>home</c>.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneKeepsTheOriginalsHome(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var home = await Dig($"CspHomeRoom{uid}");
		var original = await Create($"CspHomed{uid}");
		await AsGod($"@link {original}={home}");

		var clone = await Clone(throughTheFunction, original, $"CspHomeClone{uid}");

		var cloneHome = (await (await Node(clone)).Expect<SharpThing>().Home.WithCancellation(CancellationToken.None))
			.Object().DBRef;
		await Assert.That(cloneHome.Number).IsEqualTo(home.Number)
			.Because("create.c:657 copies the original's home onto the clone");
	}

	/// <summary>An original of each clonable type, since <c>clone_object</c> is type-blind and the
	/// exit branch sets the zone and parent of its own accord (create.c:636-637, :788-789).</summary>
	private async Task<DBRef> OriginalOfKind(string kind, string name) => kind switch
	{
		"room" => await Dig(name),
		"exit" => Ref(await AsGod($"@open {name}={await Dig($"{name}Dest")}")),
		_ => await Create(name)
	};

	/// <summary>
	/// <c>Parent(clone) = Parent(thing)</c> — <c>clone_object</c> at create.c:637 for a thing or a room,
	/// and the exit branch again at :789.
	/// </summary>
	[Test]
	[Arguments(true, "thing")]
	[Arguments(false, "thing")]
	[Arguments(true, "room")]
	[Arguments(false, "room")]
	[Arguments(true, "exit")]
	[Arguments(false, "exit")]
	public async ValueTask ACloneKeepsTheOriginalsParent(bool throughTheFunction, string kind)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var parent = await Create($"CspParent{uid}");
		var original = await OriginalOfKind(kind, $"CspChild{uid}");
		await AsGod($"@parent {original}={parent}");

		var clone = await Clone(throughTheFunction, original, $"CspParentClone{uid}");

		var clonedParent = await (await Node(clone)).Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(clonedParent.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(parent.Number)
			.Because("create.c:637 copies the parent");
	}

	/// <summary>
	/// <c>Zone(clone) = Zone(thing)</c> (create.c:636, and :788 for an exit) — the original's zone, not
	/// the cloner's.
	/// </summary>
	[Test]
	[Arguments(true, "thing")]
	[Arguments(false, "thing")]
	[Arguments(true, "room")]
	[Arguments(false, "room")]
	[Arguments(true, "exit")]
	[Arguments(false, "exit")]
	public async ValueTask ACloneKeepsTheOriginalsZone(bool throughTheFunction, string kind)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var zone = await Create($"CspZone{uid}");
		var original = await OriginalOfKind(kind, $"CspZoned{uid}");
		await AsGod($"@chzone {original}={zone}");

		var clone = await Clone(throughTheFunction, original, $"CspZoneClone{uid}");

		var clonedZone = await (await Node(clone)).Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(clonedZone.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(zone.Number)
			.Because("create.c:636 copies the zone");
	}

	/// <summary>
	/// do_clone's exit branch (create.c:765-780) re-opens the exit at the original's destination —
	/// <c>do_real_open(player, name, unparse_dbref(Location(thing)), …)</c>. SharpMUSH opened the clone
	/// with no destination at all, so a cloned exit led nowhere.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AClonedExitKeepsItsDestination(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var destination = await Dig($"CspExitDest{uid}");
		var exit = Ref(await AsGod($"@open CspExit{uid}={destination}"));

		var clone = await Clone(throughTheFunction, exit, $"CspExitClone{uid}");

		var route = await (await Node(clone)).Expect<SharpExit>().Home.WithCancellation(CancellationToken.None);
		await Assert.That(AnyOptionalSharpContainer.RefOf(route)?.Number).IsEqualTo(destination.Number)
			.Because("create.c:765-780 re-opens the clone onto the original's destination");
	}

	/// <summary>
	/// <c>real_did_it(player, clone, …, "ACLONE", …)</c> (create.c:727, :742). The attribute is seeded
	/// and was referenced by no code at all, so cloning queued nothing. It runs on the clone, with the
	/// cloner as enactor.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CloningAThingQueuesAcloneOnTheClone(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = await Create($"CspAclone{uid}");
		// STATE is written last, so waiting on it waits for the whole list.
		await AsGod($"&ACLONE {original}=&WHO me=%#;&STATE me=ran");

		var clone = await Clone(throughTheFunction, original, $"CspAcloneClone{uid}");

		await WaitForAsync(clone, "STATE", "ran");
		await Assert.That(await GetAsync(clone, "WHO")).StartsWith("#1")
			.Because("real_did_it's enactor is the cloner");
		await Assert.That(await GetAsync(original, "STATE")).IsEmpty()
			.Because("ACLONE is queued on the clone, not on the object it was taken from");
	}

	/// <summary>A cloned room queues it too (create.c:742).</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CloningARoomQueuesAclone(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = await Dig($"CspAcloneRoom{uid}");
		await AsGod($"&ACLONE {original}=&STATE me=ran");

		var clone = await Clone(throughTheFunction, original, $"CspAcloneRoomClone{uid}");

		await WaitForAsync(clone, "STATE", "ran");
	}

	/// <summary>
	/// The exit branch does not: it returns from create.c:806 without ever reaching a
	/// <c>real_did_it</c>, unlike the thing and room branches above it. Queueing one for an exit would
	/// be a SharpMUSH invention, so the absence is asserted rather than assumed.
	/// </summary>
	[Test]
	public async ValueTask CloningAnExitQueuesNoAclone()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var destination = await Dig($"CspNoAcloneDest{uid}");
		var exit = Ref(await AsGod($"@open CspNoAclone{uid}={destination}"));
		await AsGod($"&ACLONE {exit}=&STATE me=ran");

		var clone = await Clone(false, exit, $"CspNoAcloneClone{uid}");
		await DrainAsync(uid);

		await Assert.That(await GetAsync(clone, "STATE")).IsEmpty()
			.Because("create.c's exit branch has no real_did_it of its own");
	}

	/// <summary>
	/// <c>queue_event(player, "OBJECT`CREATE", "%s,%s", objid(clone), objid(thing))</c>
	/// (create.c:663-664): the clone, and what it was cloned from. The command fired neither argument
	/// because it fired no event at all.
	/// </summary>
	[Test]
	[NotInParallel]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CloningFiresObjectCreateNamingWhatItWasClonedFrom(bool throughTheFunction)
	{
		// event_handler = 9 (the seeded Event Handler) in the test config.
		const int EventHandlerDbRefNumber = 9;
		var eventHandler = new DBRef(EventHandlerDbRefNumber);
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = await Create($"CspEvent{uid}");

		try
		{
			await AsGod($"&OBJECT`CREATE #{EventHandlerDbRefNumber}="
				+ $"&CREATELOG #{EventHandlerDbRefNumber}=%0|%1");

			var clone = await Clone(throughTheFunction, original, $"CspEventClone{uid}");

			var logged = await Attributes.GetAttributeAsync(eventHandler, ["CREATELOG"]).ToListAsync();
			await Assert.That(logged).IsNotEmpty()
				.Because("cloning has to fire OBJECT`CREATE like every other creation");

			var fields = logged[^1].Value.ToPlainText().Split('|');
			await Assert.That(fields[0]).StartsWith($"#{clone.Number}");
			await Assert.That(fields[1]).StartsWith($"#{original.Number}")
				.Because("create.c:663 passes the cloned-from object as the second argument");
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`CREATE");
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/CREATELOG");
		}
	}

	/// <summary>
	/// A deliberate deviation, pinned so it cannot drift back by accident: an exit clone names its
	/// cloned-from object too. Penn's exit branch reaches the event through <c>do_real_open</c>
	/// (<c>create.c:177</c>), which knows nothing about cloning and passes the new exit alone, while the
	/// thing and room branches pass both (<c>:663</c>). That asymmetry is an artifact of which function
	/// queues the event, not a contract a handler could sensibly rely on.
	/// </summary>
	[Test]
	[NotInParallel]
	public async ValueTask CloningAnExitAlsoNamesWhatItWasClonedFrom()
	{
		const int EventHandlerDbRefNumber = 9;
		var eventHandler = new DBRef(EventHandlerDbRefNumber);
		var uid = Guid.NewGuid().ToString("N")[..8];
		var destination = await Dig($"CspExitEventDest{uid}");
		var exit = Ref(await AsGod($"@open CspExitEvent{uid}={destination}"));

		try
		{
			await AsGod($"&OBJECT`CREATE #{EventHandlerDbRefNumber}="
				+ $"&CREATELOG #{EventHandlerDbRefNumber}=%0|%1");

			var clone = await Clone(false, exit, $"CspExitEventClone{uid}");

			var logged = await Attributes.GetAttributeAsync(eventHandler, ["CREATELOG"]).ToListAsync();
			await Assert.That(logged).IsNotEmpty();

			var fields = logged[^1].Value.ToPlainText().Split('|');
			await Assert.That(fields[0]).StartsWith($"#{clone.Number}");
			await Assert.That(fields[1]).StartsWith($"#{exit.Number}");
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`CREATE");
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/CREATELOG");
		}
	}

	/// <summary>
	/// <c>if (preserve &amp;&amp; !Wizard(player))</c> (create.c:711-714) — a mortal may not use
	/// <c>/PRESERVE</c> at all, and nothing is cloned. SharpMUSH accepted the switch from anyone.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask PreserveIsRefusedForANonWizardAndClonesNothing(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspMortal");
		var original = Ref(await Run(mortal.Handle, $"@create CspMortalOwned{uid}"));

		var newName = $"CspPreserved{uid}";
		var reported = await Run(mortal.Handle, throughTheFunction
			? $"think clone({original},{newName},,preserve)"
			: $"@clone/preserve {original}={newName}");

		await Assert.That(reported).IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		var found = (await AsGod($"think lsearch(all,name,{newName})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(found.Length).IsEqualTo(0)
			.Because("create.c:713 returns NOTHING before anything is created");
	}

	/// <summary>
	/// What <c>/PRESERVE</c> is for (create.c:643-652): the powers and the warnings come across, and
	/// the cloner is told. Without it they are zapped — which is what a fresh object has anyway, so the
	/// assertion that matters is that they are not carried.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask PreserveCarriesPowersAndWarningsAndPlainCloningDoesNot(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = await Create($"CspPowered{uid}");
		await AsGod($"@power {original}=Halt");
		await Mediator.Send(new SetObjectWarningsCommand(await Node(original), WarningType.ExitUnlinked));

		var preservedName = $"CspPreservedClone{uid}";
		var preserved = Ref(throughTheFunction
			? await AsGod($"think clone({original},{preservedName},,preserve)")
			: await AsGod($"@clone/preserve {original}={preservedName}"));

		var preservedNode = await Node(preserved);
		await Assert.That(await preservedNode.Object().HasPower("Halt")).IsTrue()
			.Because("create.c:650 clones the power bitmask when preserving");
		await Assert.That(preservedNode.Object().Warnings).IsEqualTo(WarningType.ExitUnlinked)
			.Because("create.c:651 carries the warnings when preserving");

		var plain = await Clone(throughTheFunction, original, $"CspPlainClone{uid}");
		var plainNode = await Node(plain);
		await Assert.That(await plainNode.Object().HasPower("Halt")).IsFalse()
			.Because("create.c:646-647 zaps powers and warnings without /PRESERVE");
		await Assert.That(plainNode.Object().Warnings).IsEqualTo(WarningType.None);
	}

	/// <summary>
	/// <c>do_clone</c>'s thing branch (create.c:728-731): <c>if (IsRoom(player)) moveto(clone, player)
	/// else moveto(clone, Location(player))</c>. SharpMUSH used <c>@create</c>'s rule instead — hand the
	/// object to the executor when the executor can hold one — which is true of every player, so a
	/// player's clone landed in their own inventory rather than beside the original.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneLandsInTheClonersRoomAndNotTheirInventory(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspWhere");
		var room = await Dig($"CspWhereRoom{uid}");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		var original = Ref(await Run(mortal.Handle, $"@create CspWhereSource{uid}"));
		var newName = $"CspWhereClone{uid}";
		var clone = Ref(await Run(mortal.Handle, throughTheFunction
			? $"think clone({original},{newName})"
			: $"@clone {original}={newName}"));

		var where = (await (await Node(clone)).Where()).Object().DBRef;
		await Assert.That(where.Number).IsEqualTo(room.Number)
			.Because("create.c:731 moves the clone to Location(player), not into the player");
	}

	/// <summary>
	/// The other half of create.c:728-731: code owned by a room builds into the room itself, because a
	/// room has no location of its own to fall back to.
	/// </summary>
	[Test]
	public async ValueTask ACloneMadeByARoomLandsInThatRoom()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var room = await Dig($"CspRoomCloner{uid}");
		var original = await Create($"CspRoomClonerSource{uid}");

		var newName = $"CspRoomClonerClone{uid}";
		await AsGod($"@force {room}=@clone {original}={newName}");

		var found = await Named(newName);
		await Assert.That(found.Length).IsEqualTo(1);

		var where = (await (await Node(Ref(found[0]))).Where()).Object().DBRef;
		await Assert.That(where.Number).IsEqualTo(room.Number)
			.Because("create.c:729 moves a room's clone into the room itself");
	}

	/// <summary>
	/// The exit branch (create.c:771-773) hands <c>do_real_open</c> a <c>pseudo</c> of NOTHING, so the
	/// source is <c>speech_loc(player)</c> (create.c:97, speech.c:109) and <c>do_real_open</c> refuses a
	/// source that is not a room outright (create.c:108-110), before <c>can_pay_fees</c> is reached
	/// (<c>:130</c>). Driven from a thing sitting inside another thing, because that is the only cloner
	/// this server will put somewhere that is not a room — <c>@teleport</c> refuses to move a player
	/// into one.
	/// </summary>
	[Test]
	public async ValueTask CloningAnExitFromSomewhereThatIsNotARoomIsRefused()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspInside");
		var room = await Dig($"CspInsideRoom{uid}");
		await AsGod($"@chown {room}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		var destination = await Dig($"CspInsideDest{uid}");
		var exit = Ref(await Run(mortal.Handle, $"@open CspInsideExit{uid}={destination}"));

		var vehicle = Ref(await Run(mortal.Handle, $"@create CspInsideThing{uid}"));
		var rider = Ref(await Run(mortal.Handle, $"@create CspInsideRider{uid}"));
		await Run(mortal.Handle, $"@teleport {rider}={vehicle}");

		var standingIn = (await (await Node(rider)).Where()).Object().DBRef;
		await Assert.That(standingIn.Number).IsEqualTo(vehicle.Number)
			.Because("the cloner has to actually be somewhere that is not a room");

		var commandName = $"CspInsideCmdClone{uid}";
		await Run(mortal.Handle, $"@force {rider}=@clone {exit}={commandName}");
		await Assert.That((await Named(commandName)).Length).IsEqualTo(0)
			.Because("create.c:108-110 refuses before anything is built");

		var functionName = $"CspInsideFnClone{uid}";
		await Run(mortal.Handle, $"@force {rider}=&CLONERESULT {rider}=clone({exit},{functionName})");
		await Assert.That(await GetAsync(rider, "CLONERESULT")).IsEqualTo(ErrorMessages.Returns.NotARoom);
		await Assert.That((await Named(functionName)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>do_clone</c>'s exit branch is a <c>do_real_open</c> (<c>create.c:771-773</c>), so it is held
	/// to <c>can_open_from</c> (<c>:127</c>) like any other exit and not merely to "the source is a
	/// room". Charging the clone directly let a mortal who controls an exit clone it into a room they
	/// may not open in.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CloningAnExitWhereTheClonerMayNotOpenIsRefused(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspNoOpen");

		// The exit is the mortal's own, opened in a room they own, so do_clone's controls check passes.
		var home = await Dig($"CspNoOpenHome{uid}");
		await AsGod($"@chown {home}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={home}");
		var destination = await Dig($"CspNoOpenDest{uid}");
		await AsGod($"@set {destination}=LINK_OK");
		var exit = Ref(await Run(mortal.Handle, $"@open CspNoOpenExit{uid}={destination}"));

		// Now stand somewhere God owns, which is neither controlled nor OPEN_OK.
		var elsewhere = await Dig($"CspNoOpenElsewhere{uid}");
		await AsGod($"@teleport {mortal.DbRef}={elsewhere}");

		var newName = $"CspNoOpenClone{uid}";
		await Assert.That(await Run(mortal.Handle, throughTheFunction
				? $"think clone({exit},{newName})"
				: $"@clone {exit}={newName}"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That((await Named(newName)).Length).IsEqualTo(0)
			.Because("can_open_from refuses before can_pay_fees, so nothing is built");
	}

	/// <summary>
	/// <c>Zone(clone) = Zone(thing)</c> (create.c:636, :788) is an unconditional assignment, so an
	/// original with no zone leaves the clone with none. An exit clone is the case that bites:
	/// it is built by <c>do_real_open</c>, which sets <c>Zone(new_exit) = Zone(player)</c>
	/// (create.c:131), so copying only a zone that exists would leave the cloner's behind.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneOfAZonelessExitHasNoZoneEitherAsync(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspZoneless");
		var home = await Dig($"CspZonelessHome{uid}");
		await AsGod($"@chown {home}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={home}");

		// The cloner has a zone; the exit they are about to clone has none.
		var clonerZone = await Create($"CspZonelessZone{uid}");
		await AsGod($"@chzone {mortal.DbRef}={clonerZone}");

		var destination = await Dig($"CspZonelessDest{uid}");
		await AsGod($"@set {destination}=LINK_OK");
		var exit = Ref(await Run(mortal.Handle, $"@open CspZonelessExit{uid}={destination}"));
		await AsGod($"@chzone {exit}=none");

		var newName = $"CspZonelessClone{uid}";
		var clone = Ref(await Run(mortal.Handle, throughTheFunction
			? $"think clone({exit},{newName})"
			: $"@clone {exit}={newName}"));

		var clonedZone = await (await Node(clone)).Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(clonedZone.IsNone).IsTrue()
			.Because("create.c:788 assigns the original's zone unconditionally, and it had none");
	}

	/// <summary>
	/// <c>do_clone</c>'s exit branch says why itself: "For exits, we don't want people to be able to
	/// link it to a location they can't with <c>@open</c>. So, all this stuff." (create.c:753-756).
	/// The destination goes to <c>do_real_open</c> as its <c>linkto</c>, which resolves it through
	/// <c>parse_linkable_room</c> (<c>:41-67</c>) and so through <c>can_link_to</c>. Re-linking the
	/// clone unconditionally let a mortal who controls an exit mint further exits into a destination
	/// that is no longer open to them — the room was LINK_OK when the exit was first linked, or the
	/// exit was chowned to them afterwards.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneOfAnExitIsNotLinkedWhereTheClonerMayNotLink(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CspNoLink");
		var home = await Dig($"CspNoLinkHome{uid}");
		await AsGod($"@chown {home}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={home}");

		// LINK_OK while the exit is opened, so the original really is linked...
		var destination = await Dig($"CspNoLinkDest{uid}");
		await AsGod($"@set {destination}=LINK_OK");
		var exit = Ref(await Run(mortal.Handle, $"@open CspNoLinkExit{uid}={destination}"));
		await Assert.That(await DestinationOf(exit)).IsEqualTo(destination.Number)
			.Because("the original has to be linked for the clone to have anywhere to be linked to");

		// ...and closed again before the clone is taken.
		await AsGod($"@set {destination}=!LINK_OK");

		var newName = $"CspNoLinkClone{uid}";
		var clone = Ref(await Run(mortal.Handle, throughTheFunction
			? $"think clone({exit},{newName})"
			: $"@clone {exit}={newName}"));

		await Assert.That(await DestinationOf(clone)).IsNull()
			.Because("create.c:165-171 keeps the exit and leaves it unlinked when can_link_to refuses");
		await Assert.That((await Named(newName)).Length).IsEqualTo(1)
			.Because("Penn keeps an exit it could not link, rather than refusing the clone outright");
	}

	/// <summary>
	/// "We give the clone the same modification time that its other clone has, but update the creation
	/// time" (create.c:653-655).
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask ACloneKeepsTheOriginalsModificationTime(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var original = await Create($"CspStamped{uid}");
		var originalNode = await Node(original);

		var clone = await Clone(throughTheFunction, original, $"CspStampedClone{uid}");
		var cloneNode = await Node(clone);

		await Assert.That(cloneNode.Object().ModifiedTime).IsEqualTo(originalNode.Object().ModifiedTime)
			.Because("create.c:654 copies ModTime across");
		await Assert.That(cloneNode.Object().CreationTime).IsGreaterThanOrEqualTo(originalNode.Object().CreationTime)
			.Because("create.c:655 stamps the clone's creation time with now");
	}
}
