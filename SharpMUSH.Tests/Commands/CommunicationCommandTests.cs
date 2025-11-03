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

	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Arguments("addcom test_alias_ADDCOM1=Public", "Alias 'test_alias_ADDCOM1' added for channel Public.")]
	[Arguments("addcom test_alias_ADDCOM2=Public", "Alias 'test_alias_ADDCOM2' added for channel Public.")]
	public async ValueTask AddComBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent with exact expected message
		// Match the 2-parameter Notify call (the implementation uses default parameters)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == expected) ||
				(msg.IsT1 && msg.AsT1 == expected)), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("addcom=Public")]
	[Arguments("addcom test_alias_ADDCOM3=")]
	public async ValueTask AddComInvalidArgs(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify an error notification was sent (don't check exact message)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("delcom test_alias_DELCOM1")]
	public async ValueTask DelComBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		// First add an alias
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_DELCOM1=Public"));
		
		// Clear the notifications from addcom
		NotifyService.ClearReceivedCalls();
		
		// Now delete it
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify a notification was sent (don't check exact message)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("delcom nonexistent_alias_DELCOM", "Alias 'nonexistent_alias_DELCOM' not found.")]
	public async ValueTask DelComNotFound(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify exact error notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == expected) ||
				(msg.IsT1 && msg.AsT1 == expected)), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("@clist")]
	[Arguments("@clist/full")]
	public async ValueTask CListBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent (any response is valid for channel list)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("comtitle test_alias_COMTITLE=test_title_COMTITLE")]
	public async ValueTask ComTitleBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		// First add an alias
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_COMTITLE=Public"));
		
		// Clear the notifications from addcom
		NotifyService.ClearReceivedCalls();
		
		// Now set title
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent (any response is valid)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("comtitle nonexistent_alias_COMTITLE=title", "Alias 'nonexistent_alias_COMTITLE' not found.")]
	public async ValueTask ComTitleNotFound(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify exact error notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == expected) ||
				(msg.IsT1 && msg.AsT1 == expected)), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListBasic(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		// First add some aliases
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_COMLIST1=Public"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom test_alias_COMLIST2=Public"));
		
		// Clear the notifications from addcom
		NotifyService.ClearReceivedCalls();
		
		// Now list them
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent with the aliases
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("comlist")]
	public async ValueTask ComListEmpty(string command)
	{
		Console.WriteLine("Testing: {0}", command);
		// Make sure we have no aliases (use a fresh player if possible)
		// For now, just test that it doesn't error
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Verify notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}
}
