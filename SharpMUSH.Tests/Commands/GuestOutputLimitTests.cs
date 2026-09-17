using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Guests hold the <c>Guest</c> power and get <c>guest_output_limit</c> as their output ceiling
/// instead of the 5 MB every other player has (#1024).
/// </summary>
[NotInParallel(GuestLoginTests.GuestCharacters)]
public class GuestOutputLimitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private uint GuestLimit => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.GuestOutputLimit;

	private readonly List<DBRef> _guests = [];

	// A leftover Guest power would count as a guest character in GuestLoginTests.
	[After(Test)]
	public async Task RevokeGuestPower()
	{
		foreach (var guest in _guests)
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {guest}=!Guest"));
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, bool guest)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		if (!guest) return player;
		_guests.Add(player.DbRef);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {player.DbRef}=Guest"));
		return player;
	}

	// Test players share a room, so their buckets also hold other tests' room broadcasts. Only the
	// sender tells this test's output apart from those.
	private string SaidBy(TestIsolationHelpers.TestPlayer to, DBRef sender, int from)
		=> WebAppFactoryArg.Notifications.DeliveriesFor(to.DbRef).Skip(from)
			.SingleOrDefault(delivery => delivery.Sender == sender)?.Message ?? string.Empty;

	private async Task<string> ThinkAs(TestIsolationHelpers.TestPlayer who, string code)
	{
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {code}"));
		return SaidBy(who, who.DbRef, before);
	}

	[Test]
	[Arguments("repeat(x,{0})")]
	[Arguments("ljust(x,{0})")]
	public async Task Guest_OutputOverTheGuestLimit_IsRejected(string producer)
	{
		var guest = await PlayerAsync("OutLimitGuest", guest: true);

		var said = await ThinkAs(guest, $"strlen({string.Format(producer, GuestLimit + 1)})");

		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
	}

	// Arguments are evaluated under the caller's state, but code a function parses itself (a
	// lambda's body, an iter pattern rewritten for ##) runs under the state pushed for that call.
	// The oversized value stays in a register, so no caller's result check sees it.
	[Test]
	[Arguments("u(#lambda/setq(0,repeat(x,{0})))")]
	[Arguments("iter(1,setq(0,repeat(x,{0}))[null(##)])")]
	public async Task Guest_ParsedFunctionBody_KeepsTheGuestLimit(string body)
	{
		var guest = await PlayerAsync("OutLimitGuestNested", guest: true);

		var said = await ThinkAs(guest, $"[null({string.Format(body, GuestLimit + 1)})][left(%q0,4)]");

		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
	}

	// A short-circuiting function evaluates its arguments itself and returns only a boolean, so
	// the limit an argument ran into has to halt the evaluation after the function returns.
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task LazyArgumentOverTheLimit_HaltsTheEvaluation(bool guest)
	{
		var player = await PlayerAsync("OutLimitLazy", guest);
		var limit = guest ? GuestLimit : FunctionLimits.MaxOutputCodeUnits;

		var said = await ThinkAs(player, $"cand(strlen(repeat(x,{limit + 1})))");

		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
	}

	[Test]
	public async Task Guest_RestrictedExpression_KeepsTheGuestLimit()
	{
		var guest = await PlayerAsync("OutLimitGuestRestricted", guest: true);

		var said = await ThinkAs(guest, $"restrictedexpr(space strlen,strlen(space({GuestLimit + 1})))");

		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
	}

	// A reply to @input runs the callback from its own root state, not through CommandParse. The
	// session is an object's (a guest does not control itself), started by the player's input.
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task CapturedInputCallback_KeepsThePlayersLimit(bool guest)
	{
		// Below the configured default, so only a limit read from configuration can reject it.
		const uint limit = 2048;
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Limit = options.Limit with { GuestOutputLimit = limit }
		});
		var player = await PlayerAsync("OutLimitInput", guest);
		var sessions = WebAppFactoryArg.Services.GetRequiredService<IInputSessionService>();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "OutLimitInputObj");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await WebAppFactoryArg.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(obj, obj, "CALLBACK",
			MarkupText.Plain($"@pemit %#=strlen(repeat(x,{limit + 1}))"));
		var starter = Parser.FromState(ParserState.RootFor(obj.Object().DBRef) with
		{
			Enactor = player.DbRef,
			Handle = player.Handle,
			ConnectionSessionId = ConnectionService.Get(player.Handle)!.Metadata.GetValueOrDefault("SessionId")
		});
		await Assert.That(await sessions.StartAsync(starter, obj.Object().DBRef, "CALLBACK", MarkupText.Plain("Reply:"),
			TimeSpan.FromMinutes(2))).IsNull();
		var session = sessions.GetCapturing(player.Handle)!;
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(player.DbRef);

		await sessions.DeliverAsync(Parser, session, MarkupText.Plain("reply"), false);

		var said = SaidBy(player, obj.Object().DBRef, before);
		if (guest) await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
		else await Assert.That(said).IsEqualTo((limit + 1).ToString());
	}

	// An evaluation lock's attribute runs from a root the lock service creates, with no parser of
	// the caller's to copy the limit from.
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task EvaluationLock_KeepsThePlayersLimit(bool guest)
	{
		var player = await PlayerAsync("OutLimitLock", guest);
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "OutLimitLockObj");
		var gated = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await WebAppFactoryArg.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(gated, gated, "SIZECHECK",
			MarkupText.Plain($"strlen(repeat(x,{GuestLimit + 1}))"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock {thing}=SIZECHECK/{GuestLimit + 1}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {thing}=loc({player.DbRef})"));

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"get {thing}"));

		var holder = (await Mediator.Send(new GetLocationQuery(thing))).Expect<AnySharpContainer>().Object().DBRef;
		await Assert.That(holder.Number == player.DbRef.Number).IsEqualTo(!guest);
	}

	[Test]
	public async Task EvaluationLock_ReportsTheLimitToTheEvaluationThatAskedForIt()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "OutLimitLockFlag");
		var gated = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await WebAppFactoryArg.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(gated, gated, "SIZECHECK",
			MarkupText.Plain($"strlen(repeat(x,{GuestLimit + 1}))"));
		var caller = new LimitExceededFlag();

		using (OutputCeiling.Enter(ParserState.Empty with { OutputLimit = (int)GuestLimit, LimitExceeded = caller }))
			await WebAppFactoryArg.Services.GetRequiredService<ILockEvaluationServices>().EvaluateAttributeAsync(gated, gated, "SIZECHECK");

		await Assert.That(caller.IsExceeded).IsTrue();
		await Assert.That(OutputCeiling.Current).IsNull();
	}

	[Test]
	public async Task Guest_OutputAtTheGuestLimit_IsAllowed()
	{
		var guest = await PlayerAsync("OutLimitGuestOk", guest: true);

		var said = await ThinkAs(guest, $"strlen(repeat(x,{GuestLimit}))");

		await Assert.That(said).IsEqualTo(GuestLimit.ToString());
	}

	[Test]
	public async Task Guest_QueuedCommand_KeepsTheGuestLimit()
	{
		var guest = await PlayerAsync("OutLimitGuestWait", guest: true);
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(guest.DbRef);

		await Parser.CommandParse(guest.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=think strlen(repeat(x,{GuestLimit + 1}))"));

		await WebAppFactoryArg.Notifications.WaitForDeliveryAsync(guest.DbRef, "", guest.DbRef, startIndex: before);
		await Assert.That(SaidBy(guest, guest.DbRef, before)).StartsWith(ErrorMessages.Returns.OutputTooLarge);
	}

	[Test]
	[Arguments("repeat(x,{0})")]
	[Arguments("ljust(x,{0})")]
	public async Task Mortal_OutputOverTheGuestLimit_IsAllowed(string producer)
	{
		var mortal = await PlayerAsync("OutLimitMortal", guest: false);

		var said = await ThinkAs(mortal, $"strlen({string.Format(producer, GuestLimit + 1)})");

		await Assert.That(said).IsEqualTo((GuestLimit + 1).ToString());
	}
}
