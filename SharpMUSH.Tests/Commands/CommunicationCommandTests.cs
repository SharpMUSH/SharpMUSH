using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class CommunicationCommandTests
{
	private const string TestChannelName = "Public";
	private const string TestChannelPrivilege = "Open";
	private const int TestPlayerDbRef = 1;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private SharpChannel? _testChannel;
	private SharpPlayer? _testPlayer;

	[Before(Test)]
	public async Task SetupTestChannel()
	{
		var playerNode = await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef));
		_testPlayer = playerNode.IsPlayer ? playerNode.AsPlayer : null;

		if (_testPlayer == null)
		{
			throw new InvalidOperationException($"Test player #{TestPlayerDbRef} not found");
		}

		// Create a test channel named "Public"
		await Mediator.Send(new CreateChannelCommand(
			MModule.single(TestChannelName),
			[TestChannelPrivilege],
			_testPlayer
		));

		// Retrieve the created channel
		var channelQuery = new GetChannelQuery(TestChannelName);
		_testChannel = await Mediator.Send(channelQuery);

		// Add the test player to the channel
		if (_testChannel != null && playerNode.IsPlayer)
		{
			await Mediator.Send(new AddUserToChannelCommand(_testChannel, playerNode.AsPlayer));
		}
	}

	[Test]
	[Arguments("@pemit #1=Test message", "Test message")]
	[Arguments("@pemit #1=Another test", "Another test")]
	public async ValueTask PemitBasic(string command, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), expected, TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("@emit Test broadcast", "Test broadcast")]
	[Arguments("@emit Another broadcast message", "Another broadcast message")]
	public async ValueTask EmitBasic(string command, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// @emit broadcasts to room via CommunicationService.SendToRoomAsync which calls
		// Notify(AnySharpObject, ..., NotificationType.Emit)
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expected)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);
	}

	[Test]
	[Arguments("@lemit Test local emit", "Test local emit")]
	public async ValueTask LemitBasic(string command, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expected)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);
	}

	[Test]
	[Arguments("@remit #0=Test remote emit", "Test remote emit")]
	public async ValueTask RemitBasic(string command, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expected)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);
	}

	[Test]
	public async ValueTask OemitBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: @oemit");

		// Create a unique thing to omit so that the executor (player #1) still receives the emit.
		var excludeName = TestIsolationHelpers.GenerateUniqueName("OemitExclude");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {excludeName}"));
		var excludeDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		var expectedMsg = "Test omit emit";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@oemit {excludeDbRef}={expectedMsg}"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expectedMsg)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);
	}

	[Test]
	public async ValueTask ZemitBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: @zemit");

		var expectedMsg = "Test zone emit";

		// Create a unique zone master object (ZMO).
		var zmoName = TestIsolationHelpers.GenerateUniqueName("ZemitZMO");
		var zmoResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zmoName}"));
		var zmoDbRef = DBRef.Parse(zmoResult.Message!.ToPlainText()!);

		// Zone room #0 to the ZMO so that it participates in the zone.  Player #1 is in room #0.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone #0={zmoDbRef}"));

		// Emit to the zone — player #1 (in room #0, which is now in the zone) should receive it.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@zemit {zmoDbRef}={expectedMsg}"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expectedMsg)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);

		// Clean up: remove the temporary zone from room #0.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone #0=none"));
	}

	[Test]
	[Arguments("@nsemit Test nospoof emit")]
	public async ValueTask NsemitBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, "Test nospoof emit")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSEmit);
	}

	[Test]
	[Arguments("@nslemit Test nospoof local")]
	public async ValueTask NslemitBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, "Test nospoof local")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSEmit);
	}

	[Test]
	[Arguments("@nsremit #0=Test nospoof remote")]
	public async ValueTask NsremitBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, "Test nospoof remote")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSEmit);
	}

	[Test]
	[Arguments("@nsoemit #1=Test nospoof omit")]
	public async ValueTask NsoemitBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, "Test nospoof omit")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Emit);
	}

	[Test]
	[Arguments("@nspemit #1=Test nospoof pemit")]
	public async ValueTask NspemitBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessageEquals(msg, "Test nospoof pemit")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSAnnounce);
	}

	[Test]
	public async ValueTask NszemitBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: @nszemit");

		var expectedMsg = "Test nospoof zone";

		// Create a unique zone master object (ZMO).
		var zmoName = TestIsolationHelpers.GenerateUniqueName("NsZemitZMO");
		var zmoResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zmoName}"));
		var zmoDbRef = DBRef.Parse(zmoResult.Message!.ToPlainText()!);

		// Zone room #0 to the ZMO so that it participates in the zone.  Player #1 is in room #0.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone #0={zmoDbRef}"));

		// Emit to the zone — player #1 (in room #0, which is now in the zone) should receive it.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@nszemit {zmoDbRef}={expectedMsg}"));

		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, expectedMsg)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.NSEmit);

		// Clean up: remove the temporary zone from room #0.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone #0=none"));
	}

	[Test]
	[Arguments("addcom test_alias_ADDCOM1=Public")]
	[Arguments("addcom test_alias_ADDCOM2=Public")]
	public async ValueTask AddComBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split('=')[0].Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent with message containing the alias and channel
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AliasAddedForChannelFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask AddComEmptyAlias()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom=Public"));
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AliasNameCannotBeEmpty), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask AddComChannelNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_ADDCOM3=NonExistentChannel"));
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), "Channel not found.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("delcom test_alias_DELCOM1")]
	public async ValueTask DelComBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split(' ')[1];
		// First add an alias
		await Parser.CommandParse(1, ConnectionService, MModule.single($"addcom {alias}=Public"));

		// Now delete it
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify the deletion notification was sent
		// Check for the specific deletion message (not the addcom message from earlier)
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AliasDeletedFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Arguments("delcom nonexistent_alias_DELCOM")]
	public async ValueTask DelComNotFound(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify error notification was sent
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AliasNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Arguments("@clist")]
	[Arguments("@clist/full")]
	public async ValueTask CListBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify a notification was sent (channel list output contains "Name: Public")
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Name: Public")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comtitle test_alias_COMTITLE=test_title_COMTITLE")]
	public async ValueTask ComTitleBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		var parts = command.Split('=');
		var alias = parts[0].Split(' ')[1];
		var title = parts[1];

		// First add an alias
		await Parser.CommandParse(1, ConnectionService, MModule.single($"addcom {alias}=Public"));

		// Now set title
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify the title set notification was sent containing the title and alias
		// Note: This command sends TWO notifications - one from ChannelTitle.Handle and one custom message
		// We check that at least one contains our custom message with alias information
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.TitleSetForAliasChannelFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Arguments("comtitle nonexistent_alias_COMTITLE=title")]
	public async ValueTask ComTitleNotFound(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split('=')[0].Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify error notification was sent
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AliasNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListBasic(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		// First add some aliases
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_COMLIST1=Public"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_COMLIST2=Public"));

		// Now list them
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify we received at least one notification (the comlist output)
		// The output is sent as a multi-line MString containing all aliases (in lowercase)
		// Note: Aliases are stored in uppercase but displayed in lowercase
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				msg.IsT0 &&
				msg.AsT0.ToPlainText().ToLower().Contains("test_alias_comlist1") &&
				msg.AsT0.ToPlainText().ToLower().Contains("test_alias_comlist2")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListEmpty(string command)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", command);
		// Wipe all channel aliases for the executor to ensure an empty state
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wipe me/CHANALIAS*"));

		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify the empty list message was sent
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.YouHaveNoChannelAliases), executor, executor)).IsTrue();
	}
}
