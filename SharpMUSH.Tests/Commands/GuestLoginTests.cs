using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GuestLoginTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	[Test]
	public async ValueTask ConnectGuest_NoGuestCharacters_FailsWithError()
	{
		var guestHandle = 1002L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsTrue();
		await Assert.That(resultMessage.Contains("NO GUEST CHARACTERS")).IsTrue();

		await NotifyService
		.Received(1)
		.Notify(Arg.Is<long>(h => h == guestHandle),
		Arg.Is<OneOf<MString, string>>(s =>
		TestHelpers.MessagePlainTextEquals(s, "Sorry, there are no guest characters available.")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(ConnectGuest_NoGuestCharacters_FailsWithError))]
	public async ValueTask ConnectGuest_BasicLogin_Succeeds()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;

		var playerDbRef = await Mediator.Send(new CreatePlayerCommand(
		"Guest1",
		"testpass",
		defaultHome,
		defaultHome,
		startingQuota
		));

		var player = await Mediator.CreateStream(new GetPlayerQuery("Guest1")).FirstOrDefaultAsync();
		await Assert.That(player).IsNotNull();

		var guestPower = await Mediator.Send(new GetPowerQuery("Guest"));
		await Assert.That(guestPower).IsNotNull();

		var anyPlayer = new AnySharpObject(player!);
		var setPowerResult = await Mediator.Send(new SetObjectPowerCommand(anyPlayer, guestPower!));
		await Assert.That(setPowerResult).IsTrue();

		await Task.Delay(200);

		var guestHandle = 1000L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy Guest1"));
	}

	[Test]
	[DependsOn(nameof(ConnectGuest_BasicLogin_Succeeds))]
	public async ValueTask ConnectGuest_CaseInsensitive_Succeeds()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;

		var playerDbRef = await Mediator.Send(new CreatePlayerCommand(
		"Guest2",
		"testpass",
		defaultHome,
		defaultHome,
		startingQuota
		));

		var player = await Mediator.CreateStream(new GetPlayerQuery("Guest2")).FirstOrDefaultAsync();
		var guestPower = await Mediator.Send(new GetPowerQuery("Guest"));
		await Mediator.Send(new SetObjectPowerCommand(new AnySharpObject(player!), guestPower!));

		await Task.Delay(200);

		var guestHandle = 1001L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect GUEST"));

		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy Guest2"));
	}

	[Test]
	[DependsOn(nameof(ConnectGuest_CaseInsensitive_Succeeds))]
	public async ValueTask ConnectGuest_MultipleGuests_SelectsAppropriateOne()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;

		await Mediator.Send(new CreatePlayerCommand("Guest3", "testpass", defaultHome, defaultHome, startingQuota));
		await Mediator.Send(new CreatePlayerCommand("Guest4", "testpass", defaultHome, defaultHome, startingQuota));

		var player3 = await Mediator.CreateStream(new GetPlayerQuery("Guest3")).FirstOrDefaultAsync();
		var player4 = await Mediator.CreateStream(new GetPlayerQuery("Guest4")).FirstOrDefaultAsync();
		var guestPower = await Mediator.Send(new GetPowerQuery("Guest"));

		await Mediator.Send(new SetObjectPowerCommand(new AnySharpObject(player3!), guestPower!));
		await Mediator.Send(new SetObjectPowerCommand(new AnySharpObject(player4!), guestPower!));

		await Task.Delay(200);

		var guestHandle = 1003L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy Guest3"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy Guest4"));
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires guest configuration testing infrastructure")]
	[DependsOn(nameof(ConnectGuest_MultipleGuests_SelectsAppropriateOne))]
	public async ValueTask ConnectGuest_GuestsDisabled_FailsWithError()
	{
		// This test would require modifying the configuration to disable guests
		// Skipping for now as it requires configuration testing infrastructure
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires advanced connection management")]
	[DependsOn(nameof(ConnectGuest_GuestsDisabled_FailsWithError))]
	public async ValueTask ConnectGuest_MaxGuestsReached_FailsWithError()
	{
		// This test would require:
		// 1. Setting max_guests configuration
		// 2. Creating exactly that many guest connections
		// 3. Attempting to connect one more
		// Skipping for now as it requires more complex setup
		await ValueTask.CompletedTask;
	}
}
