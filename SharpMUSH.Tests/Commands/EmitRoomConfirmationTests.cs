using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class EmitRoomConfirmationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "RoomEcho");
		_handles.Add(player.Handle);
		return player;
	}
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<CallState> Admin(string command) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
	private sealed record FixedOptions(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	[Test]
	[Arguments("emit", false, "")]
	[Arguments("emit", true, "")]
	[Arguments("emit", false, "/silent")]
	[Arguments("emit", true, "/silent")]
	[Arguments("emit", false, "/noisy")]
	[Arguments("emit", true, "/noisy")]
	[Arguments("nsemit", false, "")]
	[Arguments("nsemit", true, "")]
	[Arguments("nsemit", false, "/silent")]
	[Arguments("nsemit", true, "/silent")]
	[Arguments("nsemit", false, "/noisy")]
	[Arguments("nsemit", true, "/noisy")]
	public async Task OutermostAliasHonorsConfirmationOverrides(string command, bool silentDefault, string modifier)
	{
		var actor = await Player();
		var witness = await Player();
		var room = DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim());
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "RoomEchoContainer");
		await Admin($"@tel {container}={room}");
		await Admin($"@tel {actor.DbRef}={container}");
		await Admin($"@tel {witness.DbRef}={room}");

		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = new FixedOptions(baseline with { Compatibility = baseline.Compatibility with { SilentPEmit = silentDefault } });
		var notifications = Substitute.For<INotifyService>();
		var communication = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services, notifications);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, options, notifications, communication);
		var parser = (MUSHCodeParser)Factory.CommandParserFor(actor.DbRef, actor.Handle);
		parser = parser with { Configuration = options, CommandLibrary = commands.Get() };
		var body = $"room_echo_{Guid.NewGuid():N}";
		var result = await parser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@{command}/room{modifier} {body}"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(body);
		await Assert.That(result.HadErrors).IsFalse();
		await notifications.Received().Notify(TestHelpers.MatchingObject(witness.DbRef), TestHelpers.MatchingMessage(body),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.Emit);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(notifications,
			nameof(ErrorMessages.Notifications.YouLemitFormat), actor.DbRef, actor.DbRef))
			.IsEqualTo(modifier == "/noisy" || modifier != "/silent" && !silentDefault);
	}
}
