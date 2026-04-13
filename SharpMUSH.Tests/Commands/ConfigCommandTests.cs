using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using OneOf;

namespace SharpMUSH.Tests.Commands;

public class ConfigCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask ConfigCommand_NoArgs_ListsCategories()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config"));

		// Should notify with "Configuration Categories:"
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigCategoriesHeader), executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config Net"));

		// Should notify with "Options in Net:"
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_OptionArg_ShowsOptionValue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config mud_name"));

		// Should receive at least one notification about mud_name
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@config test_string_CONFIG_invalid_option"));

		// Should notify that option was not found
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor)).IsTrue();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MonikerCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@moniker #1=Test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Moniker set.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@motd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Usage: @motd")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ListmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@listmotd"));

		// Should notify with MOTD settings
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Message of the Day settings")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WizmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizmotd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Usage: @wizmotd <message>", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RejectmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@rejectmotd"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Usage: @rejectmotd <message>", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DoingCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@doing #1=Test activity"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(1, "God/DOING - Set.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoingPollCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("doing"));

		// Should notify with player list - verify we got a notification
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoingPollCommand_WithPattern()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("doing Wiz*"));

		// Should notify with filtered player list
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Enable_BooleanOption_ShowsImplementationMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @enable with a known boolean option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable noisy_whisper"));

		// Should notify about the equivalent @config/set command
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableEquivalentFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_BooleanOption_ShowsImplementationMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @disable with a known boolean option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable noisy_whisper"));

		// Should notify about the equivalent @config/set command
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableEquivalentFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @enable with a non-existent option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable test_string_ENABLE_invalid_option_xyz"));

		// Should notify that option was not found
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @disable with a non-existent option
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable test_string_DISABLE_invalid_option_xyz"));

		// Should notify that option was not found
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_NonBooleanOption_ReturnsInvalidType()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @enable with a non-boolean option (e.g., mud_name)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable mud_name"));

		// Should notify that it's not a boolean option
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_NonBooleanOption_ReturnsInvalidType()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @disable with a non-boolean option (e.g., probate_judge)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable probate_judge"));

		// Should notify that it's not a boolean option
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @enable without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable"));

		// Should show usage message
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test @disable without arguments
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable"));

		// Should show usage message
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor)).IsTrue();
	}
}
