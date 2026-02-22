using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

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
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected, Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("@emit Test broadcast", "Test broadcast")]
	[Arguments("@emit Another broadcast message", "Another broadcast message")]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask EmitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// @emit broadcasts to room, so we expect at least one notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@lemit Test local emit", "Test local emit")]
	[Skip("Not yet implemented")]
	public async ValueTask LemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@remit #0=Test remote emit", "Test remote emit")]
	[Skip("Not yet implemented")]
	public async ValueTask RemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@oemit #1=Test omit emit", "Test omit emit")]
	[Skip("Not yet implemented")]
	public async ValueTask OemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@zemit Test zone emit", "Test zone emit")]
	[Skip("Not yet implemented")]
	public async ValueTask ZemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@nsemit Test nospoof emit")]
	[Skip("Not yet implemented")]
	public async ValueTask NsemitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Just verify the command runs without error
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("@nslemit Test nospoof local")]
	[Skip("Not yet implemented")]
	public async ValueTask NslemitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("@nsremit #0=Test nospoof remote")]
	[Skip("Not yet implemented")]
	public async ValueTask NsremitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("@nsoemit #1=Test nospoof omit")]
	[Skip("Not yet implemented")]
	public async ValueTask NsoemitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("@nspemit #1=Test nospoof pemit")]
	[Skip("Not yet implemented")]
	public async ValueTask NspemitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("@nszemit Test nospoof zone")]
	[Skip("Not yet implemented")]
	public async ValueTask NszemitBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Arguments("addcom test_alias_ADDCOM1=Public")]
	[Arguments("addcom test_alias_ADDCOM2=Public")]
	public async ValueTask AddComBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split('=')[0].Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent with message containing the alias and channel
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains($"Alias '{alias}' added for channel Public")) ||
				(msg.IsT1 && msg.AsT1.Contains($"Alias '{alias}' added for channel Public"))),
				null);
	}

	[Test]
	[Arguments("addcom=Public", "Alias name cannot be empty."), Skip("TODO")]
	[Arguments("addcom test_alias_ADDCOM3=NonExistentChannel", "Channel not found.")]
	public async ValueTask AddComInvalidArgs(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify error notification was sent containing the expected text
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains(expected)) ||
				(msg.IsT1 && msg.AsT1.Contains(expected))),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("delcom test_alias_DELCOM1")]
	public async ValueTask DelComBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split(' ')[1];
		// First add an alias
		await Parser.CommandParse(1, ConnectionService, MModule.single($"addcom {alias}=Public"));

		// Now delete it
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify the deletion notification was sent
		// Check for the specific deletion message (not the addcom message from earlier)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains($"Alias '{alias}' deleted")) ||
				(msg.IsT1 && msg.AsT1.Contains($"Alias '{alias}' deleted"))),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("delcom nonexistent_alias_DELCOM")]
	public async ValueTask DelComNotFound(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify error notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains($"Alias '{alias}' not found")) ||
				(msg.IsT1 && msg.AsT1.Contains($"Alias '{alias}' not found"))),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("@clist")]
	[Arguments("@clist/full")]
	public async ValueTask CListBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify a notification was sent (channel list output)
		// The exact format depends on ChannelList.Handle, but it should send something
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comtitle test_alias_COMTITLE=test_title_COMTITLE")]
	[Skip("TOOD")]
	public async ValueTask ComTitleBasic(string command)
	{
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
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains($"for alias '{alias}'")) ||
				(msg.IsT1 && msg.AsT1.Contains($"for alias '{alias}'"))),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comtitle nonexistent_alias_COMTITLE=title")]
	public async ValueTask ComTitleNotFound(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		var alias = command.Split('=')[0].Split(' ')[1];
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify error notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains($"Alias '{alias}' not found")) ||
				(msg.IsT1 && msg.AsT1.Contains($"Alias '{alias}' not found"))),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListBasic(string command)
	{
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				msg.IsT0 &&
				msg.AsT0.ToPlainText().ToLower().Contains("test_alias_comlist1") &&
				msg.AsT0.ToPlainText().ToLower().Contains("test_alias_comlist2")),
				null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comlist")]
	[Skip("TODO: Failing Test. Requires investigation.")]
	public async ValueTask ComListEmpty(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		// Make sure we have no aliases, just list
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify the empty list message was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText().Contains("no channel aliases")) ||
				(msg.IsT1 && msg.AsT1.Contains("no channel aliases"))),
				null, INotifyService.NotificationType.Announce);
	}
}
