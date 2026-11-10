using System.Text.RegularExpressions;
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
		var shown = await AsWizard("@config mud_name");

		await Assert.That(shown).Count().IsEqualTo(1);
		await Assert.That(shown[0]).Contains("Configuration options matching 'mud_name'");
		await Assert.That(shown[0]).Contains("mud_name");
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
	/// the word. They are drawn as the category listing is: a titled panel, with the option's category
	/// linked under it.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_OptionPrefix_ListsMatchingOptionsInAPanel()
	{
		var shown = await AsWizard("@config names_f");

		await Assert.That(shown).Count().IsEqualTo(1);
		await Assert.That(shown[0].Split('\n')).IsEquivalentTo(new[]
		{
			"+================< Configuration options matching 'names_f' >================+",
			"| names_file: names.cnf                                                      |",
			"+============================================================================+",
			"| File · All categories                                                      |",
			"+============================================================================+"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>The option names a panel lists, in the order it lists them.</summary>
	private static List<string> ListedOptions(string panel)
		=> [.. panel.Split('\n')
			.Select(line => Regex.Match(line, @"^\| (\w+):"))
			.Where(match => match.Success)
			.Select(match => match.Groups[1].Value)];

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

		var shown = await AsWizard("@config player_");

		await Assert.That(expected.Count).IsGreaterThan(1);
		await Assert.That(shown).Count().IsEqualTo(1);
		await Assert.That(ListedOptions(shown[0])).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// A flag-list option prints its flags, not <c>System.String[]</c>, and without the leading space
	/// <c>cf_flag</c> stores, which <c>config()</c> keeps.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_FlagListOption_ShowsItsFlags()
	{
		var shown = await AsWizard("@config room_flags");

		await Assert.That(shown[0]).Contains("| room_flags: no_command ");
	}

	/// <summary>An option that takes one of a fixed set lists the set under its value.</summary>
	[Test]
	public async ValueTask ConfigCommand_ChoiceOption_ListsItsChoices()
	{
		var shown = await AsWizard("@config layout_border");

		await Assert.That(shown[0]).Contains("| layout_border: mush ");
		await Assert.That(shown[0]).Contains("|                One of: double, single, heavy, rounded, mush, ascii, none ");
	}

	/// <summary>
	/// A mapping option lists one entry per line in key order, the first beside the option's name and
	/// the rest in its value column, never the dictionary's type name.
	/// </summary>
	[Test]
	public async ValueTask ConfigCommand_MappingOption_ListsOneEntryPerLine()
	{
		var shown = await AsWizard("@config command_aliases");

		await Assert.That(shown[0]).Contains("| command_aliases: @ATRCHOWN: @attrchown ");
		await Assert.That(shown[0]).Contains($"|{new string(' ', 18)}@ATRLOCK:  @attrlock ");
		await Assert.That(shown[0]).DoesNotContain("System.");
	}

	/// <summary>The category listing also shows the set a fixed-set option takes, under its value.</summary>
	[Test]
	public async ValueTask ConfigCommand_Category_ListsChoices()
	{
		var shown = string.Join('\n', await AsWizard("@config Cosmetic"));

		await Assert.That(shown).Contains("One of: any, allow, block, off");
	}

	/// <summary>The category listing shows a mapping option's entries as labelled values of their own.</summary>
	[Test]
	[Arguments("Alias", "@attrlock")]
	[Arguments("Restriction", "function_restrictions")]
	[Arguments("SitelockRules", "sitelock_rules")]
	[Arguments("Mssp", "telnet.sharpmush.com 4201")]
	public async ValueTask ConfigCommand_MappingCategory_ShowsEntries(string category, string expected)
	{
		var shown = string.Join('\n', await AsWizard($"@config {category}"));

		await Assert.That(shown).Contains(expected);
		await Assert.That(shown).DoesNotContain("System.");
	}

	/// <summary>A word that begins no option name is matched anywhere in one: <c>*names*</c>.</summary>
	[Test]
	public async ValueTask ConfigCommand_NoPrefixMatch_FallsBackToAWildcard()
	{
		var shown = await AsWizard("@config names");

		await Assert.That(ListedOptions(shown[0])).Contains("names_file");
		await Assert.That(shown[0]).Contains("| names_file:");
	}

	[Test]
	public async ValueTask ConfigCommand_InvalidOption_ReturnsNotFound()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@config test_string_CONFIG_invalid_option"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor, executor)).IsTrue();
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
