using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ConfigCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test, Skip("TODO")]
	public async ValueTask ConfigCommand_NoArgs_ListsCategories()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config"));

		// Should notify with "Configuration Categories:"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Configuration Categories:")), 
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Failing. Needs Investigation")]
	public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config Net"));

		// Should notify with "Options in Net:"
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Options in Net:")), 
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
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "mud_name")), 
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
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "No configuration category or option")),
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
				Arg.Is<OneOf.OneOf<MString,string>>(s => TestHelpers.MessageContains(s, "Message of the Day settings")), 
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
	public async ValueTask DoingPollCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("doing"));

		// Should notify with player list - verify we got a notification
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask DoingPollCommand_WithPattern()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("doing Wiz*"));

		// Should notify with filtered player list
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask Enable_BooleanOption_ShowsImplementationMessage()
	{
		// Test @enable with a known boolean option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable noisy_whisper"));

		// Should notify about the equivalent @config/set command
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("@enable") && 
					s.Value.ToString()!.Contains("@config/set") &&
					s.Value.ToString()!.Contains("noisy_whisper")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Disable_BooleanOption_ShowsImplementationMessage()
	{
		// Test @disable with a known boolean option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable noisy_whisper"));

		// Should notify about the equivalent @config/set command
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("@disable") && 
					s.Value.ToString()!.Contains("@config/set") &&
					s.Value.ToString()!.Contains("noisy_whisper")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Enable_InvalidOption_ReturnsNotFound()
	{
		// Test @enable with a non-existent option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable test_string_ENABLE_invalid_option_xyz"));

		// Should notify that option was not found
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("No configuration option")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Disable_InvalidOption_ReturnsNotFound()
	{
		// Test @disable with a non-existent option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable test_string_DISABLE_invalid_option_xyz"));

		// Should notify that option was not found
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("No configuration option")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Enable_NonBooleanOption_ReturnsInvalidType()
	{
		// Test @enable with a non-boolean option (e.g., mud_name)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable mud_name"));

		// Should notify that it's not a boolean option
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("not a boolean option")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Disable_NonBooleanOption_ReturnsInvalidType()
	{
		// Test @disable with a non-boolean option (e.g., probate_judge)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable probate_judge"));

		// Should notify that it's not a boolean option
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("not a boolean option")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Enable_NoArguments_ShowsUsage()
	{
		// Test @enable without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable"));

		// Should show usage message
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("Usage:") && 
					s.Value.ToString()!.Contains("@enable")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Disable_NoArguments_ShowsUsage()
	{
		// Test @disable without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable"));

		// Should show usage message
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(s => 
					s.Value.ToString()!.Contains("Usage:") && 
					s.Value.ToString()!.Contains("@disable")),
				Arg.Any<AnySharpObject>(), 
				Arg.Any<INotifyService.NotificationType>());
	}
}
