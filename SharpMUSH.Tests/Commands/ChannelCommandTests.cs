using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ChannelCommandTests : TestsBase
{
	private const string TestChannelName = "TestCommandChannel";
	private const string TestChannelPrivilege = "Open";
	private const int TestPlayerDbRef = 1;
	
	private INotifyService NotifyService => Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => Services.GetRequiredService<IMediator>();
	
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

		// Create a test channel
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
	[Skip("Not Yet Implemented")]
	public async ValueTask ChatCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chat {TestChannelName}=ChatCommand: Test message"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "<TestCommandChannel> ChatCommand: Test message")), Arg.Any<AnySharpObject>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ChannelCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask CemitCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@cemit {TestChannelName}=CemitCommand: Test message"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == $"<{TestChannelName}> CemitCommand: Test message") ||
				(msg.IsT1 && msg.AsT1 == $"<{TestChannelName}> CemitCommand: Test message")), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Emit);
	}

	[Test]
	public async ValueTask NscemitCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@nscemit {TestChannelName}=NscemitCommand: Test message"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("NscemitCommand: Test message")) ||
				(msg.IsT1 && msg.AsT1.Contains("NscemitCommand: Test message"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.NSEmit);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask AddcomCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom pub=Public"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DelcomCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("delcom pub"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ClistCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clist"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ComlistCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("comlist"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ComtitleCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single($"comtitle {TestChannelName}=Title"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
