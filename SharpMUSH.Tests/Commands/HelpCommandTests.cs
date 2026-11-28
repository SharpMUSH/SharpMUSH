using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class HelpCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask HelpCommandWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpWorks");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("help"));

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
				TestHelpers.MessagePlainTextContains(msg, "help newbie"), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpTopic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("help newbie"));

		// The 'newbie' entry renders to exactly this body; assert the full opening
		// paragraph verbatim rather than just the substring "MUSH", so the test fails
		// if the wrong entry, a truncated body, or unrelated content is delivered.
		const string expected =
			"If you are new to MUSHing, the help files may seem confusing. Most of them are written in a specific style, however, and once you understand it the files are extremely helpful.";

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
				TestHelpers.MessagePlainTextContains(msg, expected), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpWildcard");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("help help*"));

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
				TestHelpers.MessagePlainTextContains(msg, "Here are the entries which match 'help*'"), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
				TestHelpers.MessagePlainTextContains(msg, "help, help search, help search content search"), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// help/search finds topics whose body CONTAINS the search term (content search, not title match).
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("help/search newbie"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextContains(msg, "Matches:"), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("help nonexistenttopicxyz123"));

		// "No entry for" is the PennMUSH-compatible message.
		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextContains(msg, "No entry for"), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	[Test]
	public async ValueTask HelpWithPrefixMatchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// PennMUSH behavior: 'help newb' prefix-matches and finds 'newbie'.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("help newb"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextContains(msg, "If you are new to MUSHing"), sender: executor, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	/// <summary>
	/// The privileges a channel actually has, delivered by the in-game <c>help</c> command.
	///
	/// <para>The topic used to list join, speak, hide, open, quiet, hide_ok, loud and disabled. Only
	/// three of those are privileges. <c>src/extchat.c:118</c> is the authoritative list and it holds
	/// twelve; join, speak and hide are <em>locks</em> (<c>hdrs/extchat.h:228</c>,
	/// <c>enum clock_type</c>); and loud is a player flag (<c>src/flags.c:783</c>), consulted at
	/// <c>src/extchat.c:1539</c> to bypass the speak check, not a channel priv at all.</para>
	/// </summary>
	[Test]
	[MethodDataSource(nameof(RealChannelPrivileges))]
	public async ValueTask HelpChannelPrivsNamesARealPrivilege(string privilege)
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"HelpPriv{privilege}");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("help @channel privs list"));

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
					TestHelpers.MessagePlainTextContains(msg, privilege), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}

	/// <summary>PennMUSH's channel privileges, <c>src/extchat.c:118</c> minus the undocumented Thing alias.</summary>
	public static IEnumerable<string> RealChannelPrivileges() =>
	[
		"player", "object", "admin", "wizard", "quiet", "open",
		"hide_ok", "notitles", "nonames", "nocemit", "interact", "disabled"
	];

	/// <summary>
	/// The five channel locks, delivered by <c>help @clock</c>. The topic used to describe eight
	/// switches of which none exist, on a <c>@channel/clock</c> that is not a switch of <c>@channel</c>
	/// at all — <c>@CLOCK</c> is its own command with switches JOIN, SPEAK, MOD, SEE and HIDE, matching
	/// <c>enum clock_type</c> at <c>hdrs/extchat.h:228</c>.
	/// </summary>
	[Test]
	[Arguments("join")]
	[Arguments("speak")]
	[Arguments("see")]
	[Arguments("hide")]
	[Arguments("mod")]
	public async ValueTask HelpClockCoversEveryChannelLock(string lockName)
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"HelpClock{lockName}");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("help @clock"));

		await Assert.That(NotifyService.Recorded().Count(testPlayer.DbRef, msg =>
					TestHelpers.MessagePlainTextContains(msg, $"@clock/{lockName}"), sender: testPlayer.DbRef, type: INotifyService.NotificationType.Announce)).IsEqualTo(1);
	}
}
