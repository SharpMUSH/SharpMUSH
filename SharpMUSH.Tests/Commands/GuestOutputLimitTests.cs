using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
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

	private async Task<string> ThinkAs(TestIsolationHelpers.TestPlayer who, string code)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {code}"));
		return WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before).LastOrDefault() ?? string.Empty;
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

	[Test]
	public async Task Guest_RestrictedExpression_KeepsTheGuestLimit()
	{
		var guest = await PlayerAsync("OutLimitGuestRestricted", guest: true);

		var said = await ThinkAs(guest, $"restrictedexpr(space strlen,strlen(space({GuestLimit + 1})))");

		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
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
		var before = WebAppFactoryArg.Notifications.CountFor(guest.DbRef);

		await Parser.CommandParse(guest.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=think strlen(repeat(x,{GuestLimit + 1}))"));

		var said = string.Empty;
		for (var waited = 0; waited < 100 && said.Length == 0; waited++)
		{
			await Task.Delay(50);
			said = WebAppFactoryArg.Notifications.For(guest.DbRef).Skip(before).LastOrDefault() ?? string.Empty;
		}
		await Assert.That(said).StartsWith(ErrorMessages.Returns.OutputTooLarge);
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
