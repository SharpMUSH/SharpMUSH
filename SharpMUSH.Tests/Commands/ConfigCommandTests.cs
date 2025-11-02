using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ConfigCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask ConfigCommand_ListsCategories()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config"));

		// Should notify with category list
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Configuration Categories")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_ShowsCategoryOptions()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config net"));

		// Should notify with options in the Net category
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Options in Net")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_ShowsSpecificOption()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config mud_name"));

		// Should notify with the mud_name value
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("mud_name:")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config test_string_CONFIG_invalid_option_case1"));

		// Should notify that the option was not found
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("No configuration category or option")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask MonikerCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@moniker #1=Test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask MotdCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@motd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ListmotdCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@listmotd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WizmotdCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizmotd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask RejectmotdCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@rejectmotd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DoingCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@doing #1=Test activity"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DoingPollCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("doing"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
