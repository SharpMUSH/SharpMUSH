using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
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

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextContains(msg, ErrorMessages.Notifications.ConfigCategoriesHeader)
				&& TestHelpers.MessagePlainTextContains(msg, "Net:")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ConfigCommand_CategoryArg_ShowsCategoryOptions()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config Net"));

		await NotifyService.Received().Notify(TestHelpers.MatchingObject(executor),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextContains(msg, "Configuration: Net")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ConfigCommand_OptionArg_ShowsOptionValue()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config mud_name"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, executor)).IsTrue();
	}

	/// <summary>
	/// Runs <paramref name="command"/> as a fresh Wizard and returns everything it heard. The Wizard
	/// stands in a room of its own: in the shared start room it also hears other tests' players
	/// arrive, leave and disconnect.
	/// </summary>
	private async Task<List<string>> AsWizard(string command)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("CfgWizRoom"), god));
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "CfgWiz", home);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var before = WebAppFactoryArg.Notifications.CountFor(wizard.DbRef);
		await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(wizard.DbRef).Skip(before)];
	}

	/// <summary>
	/// <c>do_config_list</c> (<c>src/conf.c:1584-1621</c>) shows every option whose name starts with
	/// the word, as <c>config_to_string</c>'s <c>" %-40s %s"</c>.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_OptionPrefix_ListsEachMatchingOptionOnOneLine()
	{
		var shown = await AsWizard("@config names_f");

		await Assert.That(shown).IsEquivalentTo(new[] { " names_file                               names.cnf" });
	}

	/// <summary>
	/// Several matches come out in declaration order, as <c>do_config_list</c> walks its fixed
	/// <c>conftable</c>, not in the order a dictionary happens to enumerate.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_SeveralMatches_ComeOutInDeclarationOrder()
	{
		var expected = SharpMUSH.Configuration.Generated.ConfigMetadata.PropertyNames
			.Select(property => SharpMUSH.Configuration.Generated.ConfigMetadata.PropertyMetadata[property].Name)
			.Where(name => name.StartsWith("player_", StringComparison.OrdinalIgnoreCase))
			.ToList();

		var shown = (await AsWizard("@config player_")).Select(line => line.Trim().Split(' ')[0]).ToList();

		await Assert.That(expected.Count).IsGreaterThan(1);
		await Assert.That(shown).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// A flag-list option prints its flags, not <c>System.String[]</c>. Observed on PennMUSH 1.8.8
	/// (<c>80a1d5b9</c>, shipped <c>mushcnf.dst</c>): <c>cf_flag</c> stores a leading space, so the value
	/// column starts one space further right.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_FlagListOption_ShowsItsFlags()
	{
		var shown = await AsWizard("@config room_flags");

		await Assert.That(shown).IsEquivalentTo(new[] { " room_flags                                no_command" });
	}

	/// <summary>A word that begins no option name is matched anywhere in one: <c>*names*</c>.</summary>
	[Test]
	public async ValueTask ConfigCommand_NoPrefixMatch_FallsBackToAWildcard()
	{
		var shown = await AsWizard("@config names");

		await Assert.That(shown).Contains(" names_file                               names.cnf");
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
