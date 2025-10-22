using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class UtilityCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask ThinkBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("think Test output"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Test output");
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("think [add(2,3)]"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "5");
	}

	[Test]
	public async ValueTask CommentCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@@ This is a comment"));

		// Comment should not produce any output
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask LookBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("look"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask LookAtObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("look #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ExamineObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask FindCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SearchCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask EntrancesCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask StatsCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask VersionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@version"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ScanCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@scan"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DecompileCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@decompile #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WhereisCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
