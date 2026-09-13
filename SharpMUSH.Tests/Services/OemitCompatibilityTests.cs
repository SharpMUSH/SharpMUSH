using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class OemitCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);
		_handles.Add(player.Handle);
		return player;
	}
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<AnySharpObject> Node(DBRef reference) => (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
	private async Task<CallState> Command(string text) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(text));
	private bool Heard(DBRef who, string message) => Factory.Notifications.For(who).Contains(message);
	private async Task<CallState> Emit(TestIsolationHelpers.TestPlayer actor, string name, bool function, string target, string message)
	{
		var parser = Factory.CommandParserFor(actor.DbRef, actor.Handle);
		return function
			? (await parser.FunctionParse(MarkupText.Plain($"[{name}({target},{message})]")))!
			: await parser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@{name} {target}={message}"));
	}
	private async Task<DBRef> Room(params TestIsolationHelpers.TestPlayer[] players)
	{
		var result = await Command($"@dig {Guid.NewGuid():N}");
		var room = DBRef.Parse(result.Message!.ToPlainText().Trim());
		foreach (var player in players) await Command($"@tel {player.DbRef}={room}");
		return room;
	}

	[Test]
	[Arguments("oemit", false)]
	[Arguments("nsoemit", false)]
	[Arguments("oemit", true)]
	[Arguments("nsoemit", true)]
	public async Task QuotedOrdinalExcludesOnlyTheSelectedObject(string name, bool function)
	{
		var actor = await Player("OrdinalActor");
		var room = await Room(actor);
		for (var i = 0; i < 3; i++)
		{
			var coin = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "OrdinalCoin");
			await Command($"@name {coin}=coin");
			await Command($"@tel {coin}={room}");
		}
		var ordered = await Mediator.CreateStream(new GetContentsQuery(room))
			.Where(item => item.Object().Name == "coin").ToArrayAsync();
		await Assert.That(ordered.Length).IsEqualTo(3);
		var message = $"ordinal_{Guid.NewGuid():N}";
		await Emit(actor, name, function, $"{room}/\"2nd coin\"", message);
		await Assert.That(Heard(ordered[1].Object().DBRef, message)).IsFalse();
		await Assert.That(Heard(ordered[0].Object().DBRef, message)).IsTrue();
		await Assert.That(Heard(ordered[2].Object().DBRef, message)).IsTrue();
		await Assert.That(Heard(actor.DbRef, message)).IsTrue();
	}

	[Test]
	[Arguments("oemit", false)]
	[Arguments("nsoemit", false)]
	[Arguments("oemit", true)]
	[Arguments("nsoemit", true)]
	public async Task PlayerMatchingCountsOnlyImmediateMembers(string name, bool function)
	{
		var actor = await Player("PlayerOmitActor");
		var omitted = await Player("PlayerOmitted");
		var remote = await Player("PlayerRemote");
		var room = await Room(actor, omitted);
		var localName = (await Node(omitted.DbRef)).Object().Name;
		var remoteName = (await Node(remote.DbRef)).Object().Name;
		var exclusions = string.Join(' ', Enumerable.Repeat($"*{remoteName}", 11).Append($"*{localName}"));
		var message = $"player_{Guid.NewGuid():N}";
		await Emit(actor, name, function, $"{room}/{exclusions}", message);
		await Assert.That(Heard(omitted.DbRef, message)).IsFalse();
		await Assert.That(Heard(actor.DbRef, message)).IsTrue();
		await Assert.That(Heard(remote.DbRef, message)).IsFalse();
	}

	[Test]
	[Arguments("oemit", false)]
	[Arguments("nsoemit", false)]
	[Arguments("oemit", true)]
	[Arguments("nsoemit", true)]
	public async Task ExitPrefixReportsAndReturnsInvalidRoom(string name, bool function)
	{
		var actor = await Player("ExitOmitActor");
		var room = await Room(actor);
		var exit = await Mediator.Send(new CreateExitCommand("out", [], (await Node(room)).AsContainer,
			(await Node(actor.DbRef)).Expect<SharpPlayer>()));
		var message = $"exit_{Guid.NewGuid():N}";
		var result = await Emit(actor, name, function, $"{exit}/{actor.DbRef}", message);
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidRoom);
		await Assert.That(result.HadErrors).IsTrue();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(Factory.Services.GetRequiredService<INotifyService>(),
			nameof(ErrorMessages.Notifications.InvalidRoomSpecifiedDetail), actor.DbRef, actor.DbRef)).IsTrue();
		await Assert.That(Heard(actor.DbRef, message)).IsFalse();
		await Assert.That(Heard(room, message)).IsFalse();
		await Assert.That(Heard(exit, message)).IsFalse();
	}

	[Test]
	[Arguments("oemit", false, false)]
	[Arguments("nsoemit", false, false)]
	[Arguments("oemit", true, false)]
	[Arguments("nsoemit", true, false)]
	[Arguments("oemit", false, true)]
	[Arguments("nsoemit", false, true)]
	[Arguments("oemit", true, true)]
	[Arguments("nsoemit", true, true)]
	public async Task ThingAndPlayerContainersReceiveTheirOwnContents(string name, bool function, bool playerContainer)
	{
		var actor = await Player("ContainerOmitActor");
		var witness = await Player("ContainerOmitWitness");
		var container = playerContainer ? (await Player("ContainerOmitCarrier")).DbRef
			: await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "OmitContainer");
		await Command($"@tel/inside {witness.DbRef}={container}");
		await Assert.That((await (await Node(witness.DbRef)).Where()).Object().DBRef).IsEqualTo(container);
		var message = $"container_{Guid.NewGuid():N}";
		var result = await Emit(actor, name, function, $"{container}/unmatched", message);
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(Heard(witness.DbRef, message)).IsTrue();
		await Assert.That(Heard(container, message)).IsTrue();
		await Assert.That(Heard(actor.DbRef, message)).IsFalse();
	}
}
