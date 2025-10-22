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
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@emit Test broadcast", "Test broadcast")]
	[Arguments("@emit Another broadcast message", "Another broadcast message")]
	public async ValueTask EmitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@lemit Test local emit", "Test local emit")]
	public async ValueTask LemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@remit #0=Test remote emit", "Test remote emit")]
	public async ValueTask RemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@oemit #1=Test omit emit", "Test omit emit")]
	public async ValueTask OemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@zemit Test zone emit", "Test zone emit")]
	public async ValueTask ZemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), expected);
	}

	[Test]
	[Arguments("@nsemit Test nospoof emit", "Test nospoof emit")]
	public async ValueTask NsemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}

	[Test]
	[Arguments("@nslemit Test nospoof local", "Test nospoof local")]
	public async ValueTask NslemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}

	[Test]
	[Arguments("@nsremit #0=Test nospoof remote", "Test nospoof remote")]
	public async ValueTask NsremitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}

	[Test]
	[Arguments("@nsoemit #1=Test nospoof omit", "Test nospoof omit")]
	public async ValueTask NsoemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}

	[Test]
	[Arguments("@nspemit #1=Test nospoof pemit", "Test nospoof pemit")]
	public async ValueTask NspemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}

	[Test]
	[Arguments("@nszemit Test nospoof zone", "Test nospoof zone")]
	public async ValueTask NszemitBasic(string command, string expected)
	{
		Console.WriteLine("Testing: {0}", command);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains(expected)));
	}
}
