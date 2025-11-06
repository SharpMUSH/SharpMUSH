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

	[Test, Skip("TODO")]
	public async ValueTask ConfigCommand_NoArgs_ListsCategories()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config"));

		// Should notify with "Configuration Categories:"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Configuration Categories:")), 
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config Net"));

		// Should notify with "Options in Net:"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Options in Net:")), 
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_OptionArg_ShowsOptionValue()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config mud_name"));

		// Should receive at least one notification about mud_name
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("mud_name")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config test_string_CONFIG_invalid_option"));

		// Should notify that option was not found
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("No configuration category or option")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
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
	public async ValueTask ListmotdCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@listmotd"));

		// Should notify with MOTD settings
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => s.Value.ToString()!.Contains("Message of the Day settings")), 
				Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
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
