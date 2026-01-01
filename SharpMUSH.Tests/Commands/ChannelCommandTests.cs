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

public class ChannelCommandTests
{
	private const string TestChannelName = "TestCommandChannel";
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
	public async ValueTask ChatCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chat {TestChannelName}=ChatCommand: Test message"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "<TestCommandChannel> ChatCommand: Test message") ||
				(msg.IsT1 && msg.AsT1 == "<TestCommandChannel> ChatCommand: Test message")), Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask ChannelCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/list"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask CemitCommand()
	{
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("addcom pub=Public"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DelcomCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("delcom pub"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ClistCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clist"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ComlistCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("comlist"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ComtitleCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single($"comtitle {TestChannelName}=Title"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
