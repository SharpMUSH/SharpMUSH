using System.Text.RegularExpressions;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled bboards package, installed from the catalogue and driven by typed commands. The package is
/// installed once for the class and removed after its last test; each test makes a board of its own. Not in
/// parallel: the commands it adds are global.
/// </summary>
[NotInParallel]
public partial class BBoardsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	[GeneratedRegex(@"Posted (\S+), ")]
	private static partial Regex PostedLabel();

	private async Task<string> God(string command)
	{
		var said = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;
		await WebAppFactoryArg.QueueBarrierAsync();
		return said;
	}

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		var told = string.Join("\n", Notifications.For(player.DbRef).Skip(before));
		await Assert.That(told).DoesNotContain("notice(").Because("every message is drawn by notice(), not shown as its call");
		await Assert.That(told).DoesNotContain("#-1").Because("no function the screens call fails");
		return told;
	}

	/// <summary>
	/// What <paramref name="player"/> was told after typing <paramref name="line"/> at its connection,
	/// the way a client sends it, so an @input prompt such as the reader's takes the line.
	/// </summary>
	private async Task<string> Typed(TestIsolationHelpers.TestPlayer player, string line)
	{
		var before = Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
			.AdmitUserCommand(player.Handle, MarkupText.Plain(line), ParserState.Empty with { Handle = player.Handle });
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>
	/// Types <paramref name="line"/> as <see cref="Typed"/> does, then waits until the player is told
	/// <paramref name="expect"/>: an @input line reaches the session through the queue more than once.
	/// </summary>
	private async Task<string> TypedUntil(TestIsolationHelpers.TestPlayer player, string line, string expect)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Typed(player, line);
		await Notifications.WaitForAsync(player.DbRef, expect, startIndex: before);
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected player in a room of its own, holding <paramref name="role"/> when one is named.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string? role = null)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (role is not null)
		{
			await God($"@role/assign #{player.DbRef.Number}={role}");
		}

		return player;
	}

	// The world the package was installed on, so the class's last hook can take it out again.
	private static ServerWebAppFactory? _installedOn;

	/// <summary>Installs <paramref name="package"/> from the catalogue unless the game has it already.</summary>
	private async Task EnsureInstalledAsync(string package = "bboards")
	{
		if (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetInstalledPackageAsync(package) is InstalledPackageRecord)
		{
			return;
		}

		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		_installedOn = WebAppFactoryArg;
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it seeds the boards and adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Before(Test)]
	public Task InstallBBoardsAsync() => EnsureInstalledAsync();

	[After(Class)]
	public static async Task UninstallBBoardsAsync()
	{
		// Gone with its world when this class was the last to use it.
		if (_installedOn is { IsRunning: true } factory)
		{
			await UninstallAsync(factory);
			_installedOn = null;
		}
	}

	private static async Task UninstallAsync(ServerWebAppFactory factory)
	{
		await factory.Services.GetRequiredService<IPackageInstallService>().UninstallAsync("bboards-app", force: true, CancellationToken.None);
		await factory.Services.UninstallWithBridgesAsync("bboards");
	}

	/// <summary>A board of the test's own at the top, made by <paramref name="admin"/>; returns its name.</summary>
	private async Task<string> Board(TestIsolationHelpers.TestPlayer admin, string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		await Assert.That(await As(admin, $"+bbnewgroup {name}")).Contains("Made board");
		return name;
	}

	private async Task<string> Post(TestIsolationHelpers.TestPlayer player, string board, string title, string text)
	{
		var said = await As(player, $"+bbpost {board}/{title}={text}");
		var match = PostedLabel().Match(said);
		await Assert.That(match.Success).IsTrue().Because(said);
		return match.Groups[1].Value;
	}

	[Test]
	public async Task PostsAndCommentsKeepTheirTextAndRepliesSitUnderWhatTheyAnswer()
	{
		var admin = await Player("BbAdm", "bboard-admin");
		var mira = await Player("BbMira");
		var board = await Board(admin, "BbTalk");

		var post = await Post(mira, board, "Hello", "Text with \\[add(1,2)\\] and 50\\%.");
		await As(admin, $"+bbcomment {post}=First comment.");
		await As(admin, $"+bbcomment {post}=Second comment.");
		await As(mira, $"+bbreply {post}/1=Answer to the first.");

		var read = await As(admin, $"+bbread {post}");
		await Assert.That(read).Contains("Text with [add(1,2)] and 50%.")
			.Because("typed text is evaluated once, so escaped brackets are stored as written and a bare [text] is not a help link");
		var first = read.IndexOf("First comment.", StringComparison.Ordinal);
		var answer = read.IndexOf("Answer to the first.", StringComparison.Ordinal);
		var second = read.IndexOf("Second comment.", StringComparison.Ordinal);
		await Assert.That(first).IsGreaterThanOrEqualTo(0).Because(read);
		await Assert.That(answer).IsGreaterThan(first).Because(read);
		await Assert.That(second).IsGreaterThan(answer).Because("a reply sits under the comment it answers");

		await Assert.That(await As(mira, $"+bbread {post}/1")).Contains("Answer to the first.")
			.And.DoesNotContain("Second comment.").Because("a conversation is a comment and its replies");

		await Assert.That(await As(mira, $"+bbedit {post}=50\\%/half")).Contains($"Edited {post}.");
		await Assert.That(await As(admin, $"+bbread {post}")).Contains("Text with [add(1,2)] and half.");
		await Assert.That(await As(admin, $"+bbedit {post}/3=Answer/Reply")).Contains("Edited")
			.Because("an admin moderates every board");
	}

	/// <summary>The first-install code run again, as a package attaching to the commands object once did, makes no second set of boards.</summary>
	[Test]
	public async Task TheStartingBoardsAreMadeOnce()
	{
		// The other tests' boards sit at the top beside the starting ones, so this counts on a fresh install.
		await UninstallAsync(WebAppFactoryArg);
		await EnsureInstalledAsync();
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("bboards");
		var commands = objects.Single(o => o.Ref == "bboards").Objid;
		var store = objects.Single(o => o.Ref == "store").Objid;
		async Task<string> Boards() => (await WebAppFactoryArg.FunctionParser.EvaluateAsync(
			MarkupText.Plain($"[words(get({store}/ORDER))] [words(get({store}/CATS))]"))).ToPlainText();
		await Assert.That(await Boards()).IsEqualTo("6 2");

		await God($"@trigger {commands}/AINSTALL");

		await Assert.That(await Boards()).IsEqualTo("6 2");
	}

	/// <summary>A removed comment without replies is gone: the comments after it move up one.</summary>
	[Test]
	public async Task ARemovedCommentIsGoneAndTheNextMovesUp()
	{
		var admin = await Player("BbRmAdm", "bboard-admin");
		var mira = await Player("BbRmMira");
		var board = await Board(admin, "BbRemove");

		var post = await Post(mira, board, "Gone", "A post with two comments.");
		await As(mira, $"+bbcomment {post}=Alpha comment.");
		await As(mira, $"+bbcomment {post}=Beta comment.");

		await Assert.That(await As(mira, $"+bbremove {post}/1")).Contains("[2] is now [1].");
		var read = await As(mira, $"+bbread {post}");
		await Assert.That(read).DoesNotContain("Alpha comment.").And.Contains("Beta comment.");
		await Assert.That(await As(mira, $"+bbread {post}/1")).Contains("Beta comment.")
			.Because("the comment after the removed one is number 1 now");
	}

	/// <summary>
	/// The game-wide settings (board defaults, ceilings, thread page size) are +config settings; a board's
	/// own +bbconfig still overrides the defaults.
	/// </summary>
	[Test]
	public async Task SettingsAreChangedThroughPlusConfig()
	{
		var admin = await Player("BbCfgAdm", "bboard-admin");
		var board = await Board(admin, "BbCfg");

		try
		{
			await God("+config bboards/default-pins=4");
			await God("+config bboards/max-pins=6");
			await God("+config bboards/thread-page=5");
			await Assert.That(await As(admin, $"+bbinfo {board}")).Contains("0/4").Because("a board that sets no pins takes the default");
			await Assert.That(await As(admin, $"+bbconfig {board}/pins=8")).Contains("Pins are 0 to 6.");
			await As(admin, $"+bbconfig {board}/pins=2");
			await Assert.That(await As(admin, $"+bbinfo {board}")).Contains("0/2").Because("a board's own setting wins");
			await Assert.That(await As(admin, "+bbconfig default/pins=3")).Contains("+config bboards/default-pins");

			var post = await Post(admin, board, "Paged", "Body.");
			for (var i = 1; i <= 7; i++)
			{
				await As(admin, $"+bbcomment {post}=Setting comment {i}.");
			}

			await Assert.That(await As(admin, $"+bbread {post}")).Contains("page 1 of 2");
		}
		finally
		{
			// The package stays installed for the next test, so its settings go back.
			foreach (var setting in new[] { "default-pins", "max-pins", "thread-page" })
			{
				await God($"+config/reset bboards/{setting}");
			}
		}
	}

	/// <summary>
	/// Removing a post, tossing a draft and clearing a board take the readers' leaves for them away, not
	/// leave them behind empty.
	/// </summary>
	[Test]
	public async Task RemovedPostsDraftsAndBoardsLeaveNoEmptyReadingLeaves()
	{
		var admin = await Player("BbLfAdm", "bboard-admin");
		var mira = await Player("BbLfMira");
		var board = await Board(admin, "BbLeaves");
		var post = await Post(admin, board, "Followed", "Follow me.");

		await Assert.That(await As(mira, $"+bbfollow {post}")).Contains("Following");
		await Assert.That(await God($"think [words(lattr(#{mira.DbRef.Number}/BBOARD`P**))]")).IsNotEqualTo("0");
		await As(admin, $"+bbremove {post}");
		await Assert.That(await God($"think [lattr(#{mira.DbRef.Number}/BBOARD`P**)]")).IsEmpty()
			.Because("a removed post's reading leaves are wiped, not emptied");

		await As(mira, $"+bbpost {board}/A draft");
		await As(mira, "+bbwrite A line.");
		await Assert.That(await God($"think [words(lattr(#{mira.DbRef.Number}/BBOARD`DRAFT**))]")).IsNotEqualTo("0");
		await As(mira, "+bbtoss");
		await Assert.That(await God($"think [lattr(#{mira.DbRef.Number}/BBOARD`DRAFT**)]")).IsEmpty()
			.Because("a tossed draft is wiped, not emptied");

		var group = await Board(admin, "BbLeavesGone");
		await As(mira, $"+bbleave {group}");
		await Assert.That(await God($"think [words(lattr(#{mira.DbRef.Number}/BBOARD`B**))]")).IsNotEqualTo("0");
		await As(admin, $"+bbcleargroup {group}");
		await Assert.That(await God($"think [lattr(#{mira.DbRef.Number}/BBOARD`B**)]")).IsEmpty()
			.Because("a cleared board's reading leaves are wiped, not emptied");
	}

	/// <summary>
	/// A board's ON` hooks are a contract with the game's own code: each argument comes by name and by
	/// place, so a hook written either way hears the same thing.
	/// </summary>
	[Test]
	public async Task BoardHooksGetTheirArgumentsByNameAndByPlace()
	{
		var admin = await Player("BbHookAdm", "bboard-admin");
		var actor = await Objid(admin);
		var board = await Board(admin, "BbHook");
		var (_, key) = await BoardRow(admin, board);
		await God($"&ON`POST {key}=&HEARD`POST me=%<post>|%0|%<actor>|%1|%<path>|%2|%<title>|%3");
		await God($"&ON`COMMENT {key}=&HEARD`COMMENT me=%<post>|%0|%<actor>|%1|%<path>|%2|%<comment>|%3|%<reply>|%4");
		await God($"&ON`PIN {key}=&HEARD`PIN me=%<post>|%0|%<actor>|%1|%<pinned>|%2");

		var post = await Post(admin, board, "Hooked, by name", "Body.");
		var heard = (await God($"think [get({key}/HEARD`POST)]")).Split('|');
		await Assert.That(heard.Length).IsEqualTo(8).Because(string.Join("|", heard));
		var postObjid = heard[0];
		await Assert.That(postObjid).StartsWith("#").And.IsEqualTo(heard[1]);
		await Assert.That(heard[2]).IsEqualTo(actor).And.IsEqualTo(heard[3]);
		await Assert.That(heard[4]).IsEqualTo(post).And.IsEqualTo(heard[5]);
		await Assert.That(heard[6]).IsEqualTo("Hooked, by name").And.IsEqualTo(heard[7])
			.Because("a title holding a comma arrives whole, by name and by place");

		await As(admin, $"+bbcomment {post}=First.");
		await As(admin, $"+bbreply {post}/1=Answer.");
		heard = (await God($"think [get({key}/HEARD`COMMENT)]")).Split('|');
		await Assert.That(heard.Length).IsEqualTo(10).Because(string.Join("|", heard));
		await Assert.That(heard[0]).IsEqualTo(postObjid).And.IsEqualTo(heard[1]);
		await Assert.That(heard[2]).IsEqualTo(actor).And.IsEqualTo(heard[3]);
		await Assert.That(heard[4]).IsEqualTo(post).And.IsEqualTo(heard[5]);
		await Assert.That(heard[6]).IsNotEmpty().And.IsEqualTo(heard[7]);
		await Assert.That(heard[8]).IsNotEmpty().And.IsEqualTo(heard[9])
			.Because("a reply names the comment it answers");

		await As(admin, $"+bbpin {post}");
		heard = (await God($"think [get({key}/HEARD`PIN)]")).Split('|');
		await Assert.That(string.Join("|", heard)).IsEqualTo($"{postObjid}|{postObjid}|{actor}|{actor}|1|1");
	}

	[Test]
	public async Task TypedSharpMushTextIsEvaluatedOnceThroughDraftsAndEdits()
	{
		var admin = await Player("BbAdm", "bboard-admin");
		var mira = await Player("BbOnce");
		var board = await Board(admin, "BbOnce");

		await As(mira, $"+bbpost/mush {board}/Drafted");
		await As(mira, "+bbwrite \\[add(1,2)\\] by [name(me)], 50\\%");
		await As(mira, "+bbwrite old line");
		await Assert.That(await As(mira, "+bbedit text=old/new")).Contains("Draft edited.");
		await Assert.That(await As(mira, "+bbproof")).Contains($"[add(1,2)] by {mira.Name}, 50%").And.Contains("new line");
		var drafted = PostedLabel().Match(await As(mira, "+bbpost")).Groups[1].Value;
		var read = await As(admin, $"+bbread {drafted}");
		await Assert.That(read).Contains($"[add(1,2)] by {mira.Name}, 50%").And.Contains("new line")
			.Because("a +bbwrite line is evaluated as it is typed, and never again when the draft is posted");

		var typed = await Post(mira, board, "Mush", "x");
		await As(mira, $"+bbedit/all/mush {typed}=\\[add(1,2)\\] and [add(2,2)] old");
		await Assert.That(await As(admin, $"+bbread {typed}")).Contains("[add(1,2)] and 4 old");
		await Assert.That(await As(mira, $"+bbedit {typed}=old/new")).Contains($"Edited {typed}.");
		await Assert.That(await As(admin, $"+bbread {typed}")).Contains("[add(1,2)] and 4 new")
			.Because("a partial edit changes the text as shown and evaluates nothing again");
	}

	[Test]
	public async Task AModeratorsEditRunsAsTheModeratorAndSeesAnonymousAuthors()
	{
		var admin = await Player("BbAdm", "bboard-admin");
		var mira = await Player("BbMira");
		var reader = await Player("BbRead");
		var board = await Board(admin, "BbMod");

		var post = await Post(mira, board, "Mine", "plain");
		await As(mira, $"+bbedit/all/mush {post}=Written by [name(me)].");
		await Assert.That(await As(reader, $"+bbread {post}")).Contains($"Written by {mira.Name}.");
		await Assert.That(await As(admin, $"+bbedit {post}=Written/Typed")).Contains($"Edited {post}.");
		await Assert.That(await As(reader, $"+bbread {post}")).Contains($"Typed by {mira.Name}.")
			.Because("a partial edit changes the text as shown and runs none of the author's code");
		await Assert.That(await As(admin, $"+bbedit/all/mush {post}=Edited by [name(me)]."))
			.Contains($"Edited {post}.");
		await Assert.That(await As(reader, $"+bbread {post}")).Contains($"Edited by {admin.Name}.")
			.Because("a moderator's SharpMUSH text runs as the moderator, never as the post's author");

		await As(admin, $"+bbconfig {board}/anonymous=Ghost");
		var hidden = await Post(mira, board, "Who", "Guess.");
		var seen = await As(reader, $"+bbread {hidden}");
		await Assert.That(seen).Contains("Ghost").And.DoesNotContain(mira.Name);
		await Assert.That(await As(admin, $"+bbread {hidden}")).Contains($"Ghost ({mira.Name})")
			.Because("moderators still see who wrote an anonymous post");
	}

	[Test]
	public async Task ANumberThatChangedStopsTheCommandOnce()
	{
		var admin = await Player("BbNumA", "bboard-admin");
		var mira = await Player("BbNumM");
		var board = await Board(admin, "BbNums");

		await Post(mira, board, "One", "First.");
		var two = await Post(mira, board, "Two", "Second.");
		await As(mira, $"+bbread {board}");
		await As(admin, $"+bbpost {board}/Three=Third.");
		await As(admin, $"+bbpin {board}/3");

		var warned = await As(mira, $"+bbcomment {two}=On two.");
		await Assert.That(warned).Contains("have changed since you last looked").Because(warned);
		await Assert.That(await As(admin, $"+bbread {two}")).DoesNotContain("On two.")
			.Because("the warned command did nothing");

		await As(mira, $"+bbcomment {two}=On two.");
		await Assert.That(await As(mira, $"+bbread {two}")).Contains("On two.")
			.Because("typing it again goes ahead");
	}

	[Test]
	public async Task LocksDecideWhoReadsPostsAndModerates()
	{
		var admin = await Player("BbLckA", "bboard-admin");
		var mira = await Player("BbLckM");
		var board = await Board(admin, "BbLocked");

		await As(admin, $"+bblock {board}/post=#false");
		await Assert.That(await As(mira, $"+bbpost {board}/Nope=No.")).DoesNotContain("Posted");
		await As(admin, $"+bblock {board}/post=");
		var post = await Post(mira, board, "Yes", "Allowed now.");

		await Assert.That(await As(mira, $"+bbpin {post}")).DoesNotContain("Pinned")
			.Because("nobody moderates a board with no moderate lock");
		await As(admin, $"+bblock {board}/moderate=#{mira.DbRef.Number}");
		await Assert.That(await As(mira, $"+bbclose {post}")).Contains($"Closed {post} to comments.");

		await As(admin, $"+bblock {board}/read=#false");
		await Assert.That(await As(mira, $"+bbread {board}")).Contains("you can read")
			.Because("a board you can't read answers as if it were not there");
		await Assert.That(await As(mira, "+bbread")).DoesNotContain(board);
	}

	/// <summary>
	/// A refusal says what the board's lock asks for, and a draft is refused before it starts, not when
	/// it is posted.
	/// </summary>
	[Test]
	public async Task ARefusalSaysWhatTheLockAsksFor()
	{
		var admin = await Player("BbWhyA", "bboard-admin");
		var mira = await Player("BbWhyM");
		var board = await Board(admin, "BbWhy");

		await As(admin, $"+bblock {board}/post=role^approved");
		await Assert.That(await As(mira, $"+bbpost {board}/Nope=No.")).Contains("Permission denied. Posting on")
			.And.Contains($"{board} needs the approved role.");
		await Assert.That(await As(mira, $"+bbpost {board}/A draft")).Contains("needs the approved role.")
			.And.DoesNotContain("Drafting");
		await Assert.That(await God($"think [lattr(#{mira.DbRef.Number}/BBOARD`DRAFT**)]")).IsEmpty()
			.Because("no draft starts on a board the reader may not post on");

		var post = await Post(admin, board, "Closed talk", "Read only.");
		await As(admin, $"+bblock {board}/comment=#false");
		await Assert.That(await As(mira, $"+bbcomment {post}=Hello.")).Contains($"Permission denied. Nobody may comment on");
	}

	/// <summary>
	/// A comment is announced to the board's listeners as a new post is, and the post's author hears it
	/// is on their post.
	/// </summary>
	[Test]
	public async Task ACommentIsAnnouncedLikeAPost()
	{
		var admin = await Player("BbSayA", "bboard-admin");
		var mira = await Player("BbSayM");
		var listener = await Player("BbSayL");
		var board = await Board(admin, "BbSay");
		var post = await Post(admin, board, "Talk", "Say something.");

		var adminBefore = Notifications.CountFor(admin.DbRef);
		var listenerBefore = Notifications.CountFor(listener.DbRef);
		await As(mira, $"+bbcomment {post}=Something.");
		await Assert.That(string.Join("\n", Notifications.For(listener.DbRef).Skip(listenerBefore)))
			.Contains($"New BB comment ({post}/1) posted to '{board}'").And.Contains(": Talk");
		await Assert.That(string.Join("\n", Notifications.For(admin.DbRef).Skip(adminBefore)))
			.Contains($"New BB comment ({post}/1)").And.Contains("on your post");
	}

	/// <summary>
	/// Reading the conversation that holds the only new comment reads the post, so it is not new any more.
	/// </summary>
	[Test]
	public async Task ReadingTheConversationWithTheNewCommentReadsThePost()
	{
		var admin = await Player("BbConvA", "bboard-admin");
		var mira = await Player("BbConvM");
		var board = await Board(admin, "BbConv");
		var post = await Post(admin, board, "Thread", "Start.");
		await As(admin, $"+bbcomment {post}=First.");
		await As(mira, $"+bbread {post}");
		await Assert.That(await As(mira, "+bbscan")).DoesNotContain(board);

		await As(admin, $"+bbreply {post}/1=Answer.");
		await Assert.That(await As(mira, "+bbscan")).Contains(board);
		await Assert.That(await As(mira, $"+bbread {post}/2")).Contains("Answer.");
		await Assert.That(await As(mira, "+bbscan")).DoesNotContain(board)
			.Because("the conversation shown held every comment newer than the reader's mark");
	}

	/// <summary>Times are drawn in the reader's own zone, their TZ attribute.</summary>
	[Test]
	public async Task TimesAreInTheReadersZone()
	{
		var admin = await Player("BbZoneA", "bboard-admin");
		var mira = await Player("BbZoneM");
		var board = await Board(admin, "BbZone");
		var (_, key) = await BoardRow(admin, board);
		await God($"&ON`POST {key}=&HEARD`POST me=%<post>");
		var post = await Post(admin, board, "Clock", "Tick.");
		var postObjid = await God($"think [get({key}/HEARD`POST)]");
		await God($"&AT {postObjid}=0");

		await God($"&TZ #{mira.DbRef.Number}=Asia/Tokyo");
		await Assert.That(await As(mira, $"+bbread {post}")).Contains("Jan 01 09:00");
		await God($"&TZ #{mira.DbRef.Number}=America/Chicago");
		await Assert.That(await As(mira, $"+bbread {post}")).Contains("Dec 31 18:00");
	}

	[Test]
	public async Task NewPostsAndCommentsAreFoundAndCaughtUp()
	{
		var admin = await Player("BbNewA", "bboard-admin");
		var mira = await Player("BbNewM");
		var board = await Board(admin, "BbFresh");
		await As(mira, "+bbcatchup all");

		var post = await Post(admin, board, "News", "Something new.");
		await As(admin, $"+bbcomment {post}=And a comment.");

		await Assert.That(await As(mira, "+bbscan")).Contains(board);
		var next = await As(mira, "+bbnext");
		await Assert.That(next).Contains("Something new.").And.Contains("And a comment.");

		await As(mira, $"+bbcatchup {board}");
		await Assert.That(await As(mira, "+bbscan")).DoesNotContain(board);
	}

	/// <summary>
	/// With read-store set to account, what one character reads is read for every character on its account; a
	/// player on no account, and the reader's own place and drafts, stay on the character.
	/// </summary>
	[Test]
	public async Task ReadMarksFollowTheAccountWhenTheGameSaysSo()
	{
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("bboards");
		var commands = objects.Single(o => o.Ref == "bboards").Objid;
		var admin = await Player("BbAcctA", "bboard-admin");
		var first = await Player("BbAcctF");
		var second = await Player("BbAcctS");
		var loner = await Player("BbAcctL");
		var accounts = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("bbacct").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { first, second })
			await accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
		var holder = AccountHolders.RefOf(account.Id);

		await God($"&DATA`CONFIG`READ-STORE {commands}=account");
		try
		{
			var board = await Board(admin, "BbShared");
			foreach (var reader in new[] { first, second, loner })
				await As(reader, "+bbcatchup all");
			var post = await Post(admin, board, "Shared", "Read once, read by both.");
			await Assert.That(await As(second, "+bbscan")).Contains(board);

			await As(first, $"+bbread {post}");

			await Assert.That(await As(second, "+bbscan")).DoesNotContain(board).Because("the account has read it");
			await Assert.That(await As(loner, "+bbscan")).Contains(board).Because("a player on no account keeps their own marks");
			await Assert.That(await God($"think [lattr({holder}/BBOARD`P*)]")).IsNotEqualTo(string.Empty);
			await Assert.That(await God($"think [get(#{first.DbRef.Number}/BBOARD`HERE)]")).IsNotEqualTo(string.Empty)
				.Because("where a character is reading stays with the character");
			await Assert.That(await God($"think [hasattr({holder},BBOARD`HERE)]")).IsEqualTo("0");

			await God($"&DATA`CONFIG`READ-STORE {commands}=character");
			await As(admin, $"+bbremove {post}");
			await Assert.That(await God($"think [lattr({holder}/BBOARD`P*)]")).IsEqualTo(string.Empty)
				.Because("a removed post's marks are cleared from accounts, even after the game went back to characters");
		}
		finally
		{
			await God($"&DATA`CONFIG`READ-STORE {commands}=account");
		}
	}

	/// <summary>
	/// A game that kept read marks on characters keeps them when an update moves it to accounts: the update copies
	/// each character's marks to its account once, so a later update does not bring back what changed since.
	/// </summary>
	[Test]
	public async Task AnUpdateCopiesCharacterMarksToTheAccountOnce()
	{
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("bboards");
		var commands = objects.Single(o => o.Ref == "bboards").Objid;
		var admin = await Player("BbCopyA", "bboard-admin");
		var first = await Player("BbCopyF");
		var second = await Player("BbCopyS");
		var accounts = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("bbcopy").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { first, second })
			await accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
		var holder = AccountHolders.RefOf(account.Id);

		await God($"&DATA`CONFIG`READ-STORE {commands}=character");
		try
		{
			var board = await Board(admin, "BbCopy");
			foreach (var reader in new[] { first, second })
				await As(reader, "+bbcatchup all");
			var post = await Post(admin, board, "Kept", "Read before the move.");
			await As(first, $"+bbread {post}");
			await Assert.That(await As(second, "+bbscan")).Contains(board);

			await God($"&DATA`CONFIG`READ-STORE {commands}=account");
			await God($"&DATA`MARKS`COPIED {commands}=");
			await God($"@include {commands}/INC`MARKS`TOACCOUNTS");

			await Assert.That(await As(second, "+bbscan")).DoesNotContain(board).Because("the first character's mark is now the account's");
			await Assert.That(await God($"think [hasattr({holder},BBOARD`HERE)]")).IsEqualTo("0")
				.Because("where a character is reading stays with the character");

			await God($"@wipe {holder}/BBOARD`P*");
			await God($"@include {commands}/INC`MARKS`TOACCOUNTS");
			await Assert.That(await God($"think [lattr({holder}/BBOARD`P*)]")).IsEqualTo(string.Empty)
				.Because("the copy runs once");
		}
		finally
		{
			await God($"&DATA`CONFIG`READ-STORE {commands}=account");
		}
	}

	/// <summary>
	/// With read-store set to account, which numbering a character has seen stays with the character: another
	/// character on the account listing the board after it renumbers does not clear the first one's warning.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AnotherCharacterOnTheAccountListingDoesNotClearTheNumberWarning(bool secondLooks)
	{
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("bboards");
		var commands = objects.Single(o => o.Ref == "bboards").Objid;
		var admin = await Player("BbAcNumA", "bboard-admin");
		var first = await Player("BbAcNumF");
		var second = await Player("BbAcNumS");
		var accounts = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var account = (await accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("bbacnum").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { first, second })
			await accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());

		await God($"&DATA`CONFIG`READ-STORE {commands}=account");
		try
		{
			var board = await Board(admin, "BbAcNums");
			await Post(admin, board, "One", "First.");
			var two = await Post(admin, board, "Two", "Second.");
			await As(first, $"+bbread {board}");
			await As(admin, $"+bbpost {board}/Three=Third.");
			await As(admin, $"+bbpin {board}/3");
			if (secondLooks)
				await As(second, $"+bbread {board}");

			var warned = await As(first, $"+bbcomment {two}=On two.");
			await Assert.That(warned).Contains("have changed since you last looked").Because(warned);
			await Assert.That(await As(admin, $"+bbread {two}")).DoesNotContain("On two.")
				.Because("the warned command did nothing");
		}
		finally
		{
			await God($"&DATA`CONFIG`READ-STORE {commands}=account");
		}
	}

	[Test]
	public async Task ALongThreadPagesAndADeepTreeScopesDown()
	{
		var admin = await Player("BbBigA", "bboard-admin");
		var board = await Board(admin, "BbBig");

		var post = await Post(admin, board, "Busy", string.Join(" ", Enumerable.Repeat("A long post body.", 200)));
		for (var i = 1; i <= 25; i++)
		{
			await As(admin, $"+bbcomment {post}=Comment number {i}.");
		}

		var page1 = await As(admin, $"+bbread {post}");
		await Assert.That(page1).Contains("page 1 of 3").And.Contains("Comment number 10.")
			.And.DoesNotContain("Comment number 11.").Because(page1);
		var page3 = await As(admin, $"+bbread/3 {post}");
		await Assert.That(page3).Contains("Comment number 25.").And.DoesNotContain("Comment number 20.");

		var path = board;
		for (var depth = 1; depth <= 6; depth++)
		{
			await As(admin, $"+bbnewgroup {path}/Level{depth}");
			path += $"/Level{depth}";
		}

		var tree = await As(admin, $"+bblist {board}");
		await Assert.That(tree).Contains("boards below").And.DoesNotContain("Level6")
			.Because("a deep tree stops a few levels down and says how to see the rest");
		await Assert.That(await As(admin, $"+bblist {board}/Level1/Level2/Level3")).Contains("Level6");
	}

	[Test]
	public async Task GodKeepsReadingStateThoughTheBoardsCannotSetAttributesOnGod()
	{
		var admin = await Player("BbGodA", "bboard-admin");
		var board = await Board(admin, "BbGod");
		await Post(admin, board, "One", "First.");
		var two = await Post(admin, board, "Two", "Second.");
		await God($"+bbread {board}");
		await As(admin, $"+bbpost {board}/Three=Third.");
		await As(admin, $"+bbpin {board}/3");

		var marker = TestIsolationHelpers.GenerateUniqueName("GodSays");
		await God($"+bbcomment {two}={marker}");
		await Assert.That(await As(admin, $"+bbread {two}")).DoesNotContain(marker)
			.Because("the numbers changed since God looked, so the first try is stopped");
		await God($"+bbcomment {two}={marker}");
		await Assert.That(await As(admin, $"+bbread {two}")).Contains(marker)
			.Because("God's look was kept, so the guard stops God only once");
		await Assert.That(await God("think [lattr(#1/BBOARD*)]")).IsEmpty()
			.Because("God's state is kept on the readers object, not on God");
	}

	[Test]
	public async Task TheReaderTakesSingleKeysAndLeavesOnQ()
	{
		var admin = await Player("BbRdrA", "bboard-admin");
		var mira = await Player("BbRdrM");
		var board = await Board(admin, "BbKeys");
		var post = await Post(admin, board, "Keyed", "Read me with keys.");

		await TypedUntil(mira, $"+bbreader {post}", "Read me with keys.");
		await TypedUntil(mira, "c From the reader.", "Comment [1]");
		await TypedUntil(mira, "?", "Reader keys");
		await TypedUntil(mira, "c", "Every line is text now.");
		// While writing, q is a line of text: the session's exit line is .toss, so . still posts.
		await Typed(mira, "q");
		await TypedUntil(mira, ".", "Comment [2]");
		await TypedUntil(mira, "c", "Every line is text now.");
		await TypedUntil(mira, " .TOSS", "Dropped.");
		await TypedUntil(mira, "q", "Left the reader.");

		await Assert.That(await As(mira, $"+bbread {post}")).Contains("From the reader.");
		await Assert.That(await Typed(mira, "think out")).Contains("out").Because("q leaves the reader");
	}

	[Test]
	public async Task ALineTheCommentCannotHoldIsLeftOut()
	{
		var admin = await Player("BbLongA", "bboard-admin");
		var mira = await Player("BbLongM");
		var board = await Board(admin, "BbLong");
		var post = await Post(admin, board, "Long", "Answer at length.");
		var limit = int.Parse(await God("think config(max_attribute_value_length)"));
		var line = new string('a', limit / 2 + 1);

		await TypedUntil(mira, $"+bbreader {post}", "Answer at length.");
		await TypedUntil(mira, "c", "Every line is text now.");
		await Typed(mira, line);
		// The two lines together pass the attribute limit.
		await TypedUntil(mira, line, "so it is left out.");
		await TypedUntil(mira, ".toss", "Dropped.");
		await TypedUntil(mira, "q", "Left the reader.");
	}

	[Test]
	public async Task TheReaderGoesUpToTheTopAndOpensNumbersFromThere()
	{
		var admin = await Player("BbTopA", "bboard-admin");
		var mira = await Player("BbTopM");
		// Other tests' posts sit unread on their boards; +bbnext should find only this one.
		await As(mira, "+bbcatchup all");
		var board = await Board(admin, "BbTop");
		var post = await Post(admin, board, "Upward", "Read me after going up.");
		var number = post.Split('/')[0];

		await TypedUntil(mira, $"+bbreader {board}", "Upward");
		await TypedUntil(mira, "u", "+bbread <#> opens one.");
		await TypedUntil(mira, number, "Upward");
		await TypedUntil(mira, "+BBNEXT", "Read me after going up.");
		await TypedUntil(mira, $"+bbread {board}", "Upward");
		await TypedUntil(mira, "q", "Left the reader.");
	}
}
