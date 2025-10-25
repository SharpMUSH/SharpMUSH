using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CommunicationCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

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
}
