using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH gates every build on <c>can_pay_fees</c> (<c>src/predicat.c:435-463</c>), which refuses
/// guests outright and then charges <c>pay_quota</c> (<c>:601-613</c>):
/// <code>
/// if (USE_QUOTA &amp;&amp; !NoQuota(who) &amp;&amp; (curr - cost &lt; 0)) return 0;
/// </code>
/// <c>UseQuota</c> defaults true and <c>@quota</c> both stores and displays a limit, but no creation
/// path consulted either, so a mortal with no quota left could keep allocating for ever.
/// <para>SharpMUSH stores the limit and derives what is left from the objects the owner holds, where
/// Penn stores the remainder in RQUOTA and debits it. The two agree on what may be built; the derived
/// form needs no refund, so destruction and <c>@chown</c> settle themselves.</para>
/// </summary>
public class BuildingQuotaTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText()
			?? string.Empty;

	private Task<string> AsGod(string command) => Run(1, command);

	private readonly List<DBRef> _guests = [];

	// A leftover Guest power would count as a guest character in GuestLoginTests.
	[After(Test)]
	public async Task RevokeGuestPower()
	{
		foreach (var guest in _guests)
			await AsGod($"@power {guest}=!Guest");
	}

	private async Task MakeGuestAsync(DBRef player)
	{
		_guests.Add(player);
		await AsGod($"@power {player}=Guest");
	}

	/// <summary>A mortal whose limit is exactly <paramref name="slots"/> objects beyond what it owns now.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> MortalWithSlotsAsync(string prefix, int slots)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();
		var owned = await Mediator.Send(new GetOwnedObjectCountQuery(player));

		await AsGod($"@quota/set {mortal.DbRef}={owned + slots}");

		return mortal;
	}

	/// <summary>A build that happened: a real dbref, and not one of the <c>#-1</c> refusals.</summary>
	private static bool Built(string reported) => reported.StartsWith('#') && !reported.StartsWith("#-");

	private async Task<string[]> Named(string name)
		=> (await AsGod($"think lsearch(all,name,{name})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>
	/// A player is never counted against their own quota (<c>src/wiz.c:186</c>, <c>owned = -1</c>).
	/// The owner edge a player has to itself was being counted, so every limit was one short.
	/// </summary>
	[Test]
	public async ValueTask AFreshPlayerOwnsNothing()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtFresh");
		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();

		await Assert.That(await Mediator.Send(new GetOwnedObjectCountQuery(player))).IsEqualTo(0)
			.Because("wiz.c:186 starts the count at -1 so the player itself never counts");
	}

	/// <summary>The last slot is spent, and the next build of any kind is refused.</summary>
	[Test]
	[Arguments("@create BqtThing")]
	[Arguments("@dig BqtRoom")]
	public async ValueTask TheLastSlotIsSpentAndTheNextBuildIsRefused(string command)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtOne", 1);

		await Assert.That(Built(await Run(mortal.Handle, $"{command}A{uid}"))).IsTrue()
			.Because("the one slot the player has is enough for the first object");

		await Assert.That(await Run(mortal.Handle, $"{command}B{uid}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That((await Named($"{command.Split(' ')[1]}B{uid}")).Length).IsEqualTo(0)
			.Because("predicat.c:454 refuses before new_object(), so nothing is built");
	}

	/// <summary>A player with no slots at all builds nothing, through the command or the function.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AZeroQuotaRefusesEveryCreation(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtZero", 0);
		var name = $"BqtZeroThing{uid}";

		await Assert.That(await Run(mortal.Handle, throughTheFunction
				? $"think create({name})"
				: $"@create {name}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>fun_dig</c> and <c>fun_open</c> are calls to <c>do_dig</c> and <c>do_real_open</c>
	/// (<c>src/fundb.c</c>), so the function forms are charged exactly as the commands are.
	/// </summary>
	[Test]
	public async ValueTask TheFunctionFormsAreChargedToo()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtFn");

		// do_real_open puts can_open_from ahead of can_pay_fees (create.c:127-130), so the builder has
		// to own the room it stands in for the quota to be what refuses it.
		var room = DBRef.Parse((await AsGod($"@dig BqtFnHome{uid}")).Trim());
		await AsGod($"@chown {room}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();
		await AsGod($"@quota/set {mortal.DbRef}={await Mediator.Send(new GetOwnedObjectCountQuery(player))}");

		await Assert.That(await Run(mortal.Handle, $"think dig(BqtFnRoom{uid})"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That(await Run(mortal.Handle, $"think open(BqtFnExit{uid})"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That((await Named($"BqtFnRoom{uid}")).Length).IsEqualTo(0);
		await Assert.That((await Named($"BqtFnExit{uid}")).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>do_dig</c> charges the room (<c>create.c:480</c>) and then each exit again through
	/// <c>do_real_open</c> (<c>:130</c>), so a two-exit dig costs three. With room for only the room,
	/// the room is dug and the exits are refused — Penn stops where the quota does rather than rolling
	/// the room back.
	/// </summary>
	[Test]
	public async ValueTask AMultiObjectDigStopsWhereTheQuotaDoes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtDig");
		var start = DBRef.Parse((await AsGod($"@dig BqtDigStart{uid}")).Trim());

		// do_dig's exits are do_real_open calls, so can_open_from (create.c:127) is asked before
		// can_pay_fees (:130). The digger has to own the room they stand in or permission, not quota,
		// is what refuses the forward exit and this test proves nothing.
		await AsGod($"@chown {start}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={start}");

		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();
		await AsGod($"@quota/set {mortal.DbRef}={await Mediator.Send(new GetOwnedObjectCountQuery(player)) + 1}");

		await Run(mortal.Handle, $"@dig BqtDigRoom{uid}=BqtDigTo{uid},BqtDigBack{uid}");

		await Assert.That((await Named($"BqtDigRoom{uid}")).Length).IsEqualTo(1)
			.Because("the one slot paid for the room");
		await Assert.That((await Named($"BqtDigTo{uid}")).Length).IsEqualTo(0)
			.Because("do_real_open's own can_pay_fees had nothing left to charge");
		await Assert.That((await Named($"BqtDigBack{uid}")).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>@open</c> reaches <c>can_pay_fees</c> inside <c>do_real_open</c> (<c>create.c:130</c>), behind
	/// <c>can_open_from</c> (<c>:127</c>) — so the builder has to own the room they stand in for the
	/// quota to be what refuses them, exactly as in <see cref="TheFunctionFormsAreChargedToo"/>.
	/// </summary>
	[Test]
	public async ValueTask AZeroQuotaRefusesTheOpenCommand()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtOpenCmd");
		var room = DBRef.Parse((await AsGod($"@dig BqtOpenCmdHome{uid}")).Trim());
		await AsGod($"@chown {room}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();
		await AsGod($"@quota/set {mortal.DbRef}={await Mediator.Send(new GetOwnedObjectCountQuery(player))}");

		var name = $"BqtOpenCmdExit{uid}";
		await Assert.That(await Run(mortal.Handle, $"@open {name}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// <c>@open &lt;exit&gt;=&lt;destination&gt;,&lt;return exit&gt;</c> is two objects and two calls to
	/// <c>do_real_open</c>, each asking <c>can_pay_fees</c> for itself (<c>create.c:130</c>, <c>:236</c>).
	/// With one slot the forward exit is opened and the return exit is refused, the same way
	/// <see cref="AMultiObjectDigStopsWhereTheQuotaDoes"/> pins it for <c>@dig</c>.
	/// </summary>
	[Test]
	public async ValueTask TheReturnExitIsChargedOnItsOwn()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtBack", 1);
		var room = DBRef.Parse((await AsGod($"@dig BqtBackHome{uid}")).Trim());
		var destination = DBRef.Parse((await AsGod($"@dig BqtBackDest{uid}")).Trim());
		await AsGod($"@chown {room}={mortal.DbRef}");
		await AsGod($"@chown {destination}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		// The two rooms are now the mortal's, so the two slots they cost come off the limit first.
		var player = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<SharpPlayer>();
		await AsGod($"@quota/set {mortal.DbRef}={await Mediator.Send(new GetOwnedObjectCountQuery(player)) + 1}");

		await Run(mortal.Handle, $"@open BqtBackTo{uid}={destination},BqtBackFrom{uid}");

		await Assert.That((await Named($"BqtBackTo{uid}")).Length).IsEqualTo(1)
			.Because("the one slot paid for the forward exit");
		await Assert.That((await Named($"BqtBackFrom{uid}")).Length).IsEqualTo(0)
			.Because("the return exit's own can_pay_fees had nothing left to charge");
	}

	/// <summary>
	/// <c>can_pay_fees</c> refuses a guest and an exhausted quota for different reasons
	/// (<c>predicat.c:438-441</c> against <c>:453-456</c>), and <c>@dig</c> reaches the guest branch
	/// with nothing in the way — unlike <c>@clone</c>, whose <c>controls</c> check gets there first.
	/// The lifted <c>@dig</c> body reported both as the quota.
	/// </summary>
	[Test]
	[NotInParallel(GuestLoginTests.GuestCharacters)]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AGuestDigSaysPermissionAndNotQuota(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var guest = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtGuestDig");
		await MakeGuestAsync(guest.DbRef);

		var name = $"BqtGuestDug{uid}";
		await Assert.That(await Run(guest.Handle, throughTheFunction
				? $"think dig({name})"
				: $"@dig {name}"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// A guest is refused a clone, but not by <c>can_pay_fees</c>: <c>Controls</c> answers false for
	/// any holder of the guest power (<c>PermissionService.cs:390</c>), and <c>do_clone</c> asks that
	/// first (<c>create.c:700-704</c>). The guest branch of <c>can_pay_fees</c>
	/// (<c>predicat.c:438-441</c>) is therefore unreachable from here — <c>@create</c> is where it
	/// bites, which <see cref="AGuestMayNotBuildAtAll"/> pins.
	/// </summary>
	[Test]
	[NotInParallel(GuestLoginTests.GuestCharacters)]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask AGuestMayNotClone(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var guest = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtGuestClone");
		// Built before the power is granted, so the object really is the guest's own.
		var original = DBRef.Parse((await Run(guest.Handle, $"@create BqtGuestCloneSource{uid}")).Trim());
		await MakeGuestAsync(guest.DbRef);

		var name = $"BqtGuestCloned{uid}";
		await Assert.That(await Run(guest.Handle, throughTheFunction
				? $"think clone({original},{name})"
				: $"@clone {original}={name}"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>Cloning allocates an object and is charged like any other build (<c>create.c:725, :741</c>).</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CloningIsChargedToo(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtClone", 1);
		var original = DBRef.Parse((await Run(mortal.Handle, $"@create BqtCloneSource{uid}")).Trim());

		var name = $"BqtCloned{uid}";
		await Assert.That(await Run(mortal.Handle, throughTheFunction
				? $"think clone({original},{name})"
				: $"@clone {original}={name}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// The count the limit is measured against is what the owner holds now, so destroying an object
	/// gives the slot back — Penn's <c>destroy.c:642</c> refund, without a refund.
	/// </summary>
	[Test]
	public async ValueTask DestroyingAnObjectReturnsItsSlot()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtRefund", 1);
		var first = DBRef.Parse((await Run(mortal.Handle, $"@create BqtRefundA{uid}")).Trim());

		await Assert.That(await Run(mortal.Handle, $"@create BqtRefundB{uid}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);

		await Run(mortal.Handle, $"@destroy {first}");
		await Run(mortal.Handle, $"@destroy {first}");

		await Assert.That(Built(await Run(mortal.Handle, $"@create BqtRefundC{uid}"))).IsTrue()
			.Because("the slot the destroyed object held is free again");
	}

	/// <summary>
	/// <c>@chown</c> moves the object onto the new owner's count and off the old one's, which is what
	/// <c>src/set.c:226-235</c> does with <c>can_pay_fees</c> and <c>change_quota</c> in two steps.
	/// </summary>
	[Test]
	public async ValueTask ChowningMovesTheSlotToTheNewOwner()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtChown", 1);
		var thing = DBRef.Parse((await Run(mortal.Handle, $"@create BqtChownThing{uid}")).Trim());

		await Assert.That(await Run(mortal.Handle, $"@create BqtChownBlocked{uid}"))
			.IsEqualTo(ErrorMessages.Returns.BuildingQuotaExhausted);

		await AsGod($"@chown {thing}=#1");

		await Assert.That(Built(await Run(mortal.Handle, $"@create BqtChownAfter{uid}"))).IsTrue()
			.Because("the object counts against whoever owns it now");
	}

	/// <summary>
	/// <c>NoQuota(x)</c> (<c>hdrs/mushdb.h:44</c>) — the No_Quota power exempts its holder, seeded at
	/// PowerSeed.cs:30 and until now consulted only by <c>quota()</c>.
	/// </summary>
	[Test]
	public async ValueTask ANoQuotaHolderIsNotCharged()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtNoQuota", 0);
		await AsGod($"@power {mortal.DbRef}=No_Quota");

		await Assert.That(Built(await Run(mortal.Handle, $"@create BqtExempt{uid}"))).IsTrue();
	}

	/// <summary>
	/// <c>USE_QUOTA</c> is the first term of pay_quota's test (<c>predicat.c:608</c>): with the system
	/// off nothing is charged, whatever the stored limit says.
	/// </summary>
	[Test]
	public async ValueTask NothingIsChargedWhileTheQuotaSystemIsOff()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtOff", 0);

		using var _ = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { UseQuota = false }
		});

		await Assert.That(Built(await Run(mortal.Handle, $"@create BqtOffThing{uid}"))).IsTrue();
	}

	/// <summary>
	/// Wizards are <c>Hasprivs</c> and so <c>NoQuota</c>, whatever their stored limit. A throwaway
	/// wizard rather than God: the test database is shared for the session, and #1's quota is a number
	/// other tests read.
	/// </summary>
	[Test]
	public async ValueTask AWizardIsNeverCharged()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var wizard = await MortalWithSlotsAsync("BqtWizard", 0);
		await AsGod($"@set {wizard.DbRef}=WIZARD");

		await Assert.That(Built(await Run(wizard.Handle, $"@create BqtWizardThing{uid}"))).IsTrue();
	}

	/// <summary>
	/// <c>can_pay_fees</c>'s first line (<c>predicat.c:438-441</c>): a guest may not build at all, ahead
	/// of any quota arithmetic.
	/// </summary>
	[Test]
	[NotInParallel(GuestLoginTests.GuestCharacters)]
	public async ValueTask AGuestMayNotBuildAtAll()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var guest = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BqtGuest");
		await MakeGuestAsync(guest.DbRef);

		var name = $"BqtGuestThing{uid}";
		await Assert.That(await Run(guest.Handle, $"@create {name}"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}

	/// <summary>
	/// Two builds racing for one slot: exactly one may have it. The count and the creation it admits
	/// are taken together, per owner, so the loser is refused rather than both seeing the same free
	/// slot. (The engine is single-process by design — see docs/design/engine-data-trunk.md.)
	/// </summary>
	[Test]
	public async ValueTask ConcurrentBuildsCannotBothTakeTheLastSlot()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await MortalWithSlotsAsync("BqtRace", 1);

		var results = await Task.WhenAll(
			Enumerable.Range(0, 6).Select(i => Run(mortal.Handle, $"@create BqtRace{uid}_{i}")));

		var built = results.Count(Built);
		await Assert.That(built).IsEqualTo(1)
			.Because("one slot admits one object no matter how many callers ask at once");
		await Assert.That(results.Count(r => r == ErrorMessages.Returns.BuildingQuotaExhausted)).IsEqualTo(5);
	}
}
