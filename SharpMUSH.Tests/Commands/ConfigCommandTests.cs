using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ConfigCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask ConfigCommand_NoArgs_ListsCategories()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigCategoriesHeader), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config Net"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_OptionArg_ShowsOptionValue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config mud_name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config test_string_CONFIG_invalid_option"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor, executor)).IsTrue();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MonikerCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@moniker #1=Test"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Moniker set.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@motd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Usage: @motd")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ListmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@listmotd"));

		// Should notify with MOTD settings header
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ListMotdCurrentSettingsHeader), executor, executor)).IsTrue();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WizmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@wizmotd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Usage: @wizmotd <message>", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RejectmotdCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@rejectmotd"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Usage: @rejectmotd <message>", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DoingCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@doing #1=Test activity"));

		await NotifyService
			.Received(1)
			.Notify(1, "God/DOING - Set.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoingPollCommand()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DoingPoll");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("doing"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Any<SharpMessage>(), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DoingPollCommand_WithPattern()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DoingPollPat");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("doing Wiz*"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Any<SharpMessage>(), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Enable_BooleanOption_ShowsImplementationMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@enable noisy_whisper"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableEquivalentFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_BooleanOption_ShowsImplementationMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@disable noisy_whisper"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableEquivalentFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@enable test_string_ENABLE_invalid_option_xyz"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@disable test_string_DISABLE_invalid_option_xyz"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNoOptionFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_NonBooleanOption_ReturnsInvalidType()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@enable mud_name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_NonBooleanOption_ReturnsInvalidType()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@disable probate_judge"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableNotBooleanFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Enable_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@enable"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Disable_NoArguments_ShowsUsage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@disable"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.EnableDisableUsageSyntaxFormat), executor, executor)).IsTrue();
	}
}
