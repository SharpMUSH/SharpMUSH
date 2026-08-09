using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help newbie")) ||
				(msg.IsT1 && msg.AsT1.Contains("help newbie"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithTopicWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpTopic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help newbie"));

		// The 'newbie' entry renders to exactly this body; assert the full opening
		// paragraph verbatim rather than just the substring "MUSH", so the test fails
		// if the wrong entry, a truncated body, or unrelated content is delivered.
		const string expected =
			"If you are new to MUSHing, the help files may seem confusing. Most of them are written in a specific style, however, and once you understand it the files are extremely helpful.";

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains(expected)) ||
				(msg.IsT1 && msg.AsT1.Contains(expected))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithWildcardWorks()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HelpWildcard");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help help*"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Here are the entries which match 'help*'")) ||
				(msg.IsT1 && msg.AsT1.Contains("Here are the entries which match 'help*'"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("help, help query, help search, help/search, helpfile, helpfile2")) ||
				(msg.IsT1 && msg.AsT1.Contains("help, help query, help search, help/search, helpfile, helpfile2"))), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpSearchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// help/search finds topics whose body CONTAINS the search term (content search, not title match).
		await Parser.CommandParse(1, ConnectionService, MModule.single("help/search newbie"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Matches:")) ||
				(msg.IsT1 && msg.AsT1.Contains("Matches:"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpNonExistentTopic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("help nonexistenttopicxyz123"));

		// "No entry for" is the PennMUSH-compatible message.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("No entry for")) ||
				(msg.IsT1 && msg.AsT1.Contains("No entry for"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask HelpWithPrefixMatchWorks()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// PennMUSH behavior: 'help newb' prefix-matches and finds 'newbie'.
		await Parser.CommandParse(1, ConnectionService, MModule.single("help newb"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("If you are new to MUSHing")) ||
				(msg.IsT1 && msg.AsT1.Contains("If you are new to MUSHing"))), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help @channel privs"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextContains(msg, privilege)),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
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
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("help @clock"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextContains(msg, $"@clock/{lockName}")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}
}
