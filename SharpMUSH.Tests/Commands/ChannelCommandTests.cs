using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ChannelCommandTests
{
	private const string TestChannelName = "TestCommandChannel";

	/// <summary>
	/// A channel players can use needs the <c>Player</c> privilege: PennMUSH's <c>Chan_Ok_Type</c>
	/// (hdrs/extchat.h:196) admits a player only to a channel carrying it, which is why Penn's default
	/// <c>channel_flags</c> is <c>player</c>. This fixture asked for <c>Open</c> alone, which was harmless
	/// only while the type gate went unenforced.
	/// </summary>
	private static readonly string[] TestChannelPrivileges = ["Player", "Open"];
	private const int TestPlayerDbRef = 1;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private SharpChannel? _testChannel;
	private SharpPlayer? _testPlayer;

	[Before(Test)]
	public async Task SetupTestChannel()
	{
		var player = (await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef)))
			.Expect<SharpPlayer>($"test player #{TestPlayerDbRef} exists");
		_testPlayer = player;

		await Mediator.Send(new CreateChannelCommand(
			MarkupText.Plain(TestChannelName),
			TestChannelPrivileges,
			player
		));

		var channelQuery = new GetChannelQuery(TestChannelName);
		_testChannel = await Mediator.Send(channelQuery);

		if (_testChannel != null)
		{
			await Mediator.Send(new AddUserToChannelCommand(_testChannel, player));
		}
	}

	[Test]
	public async ValueTask ChatCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chat {TestChannelName}=ChatCommand: Test message"));

		// sharpchat.md:39 — `@chat pub=Hello` renders as `<Public> Mike says, "Hello"`. This test used to
		// assert the un-attributed emit form, which is what @chat produced when it published every
		// message as NotificationType.Emit regardless of the message's leading character.
		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
					TestHelpers.MessageEquals(msg, "<TestCommandChannel> God says, \"ChatCommand: Test message\""), type: INotifyService.NotificationType.Say)).IsGreaterThanOrEqualTo(1);
	}

	/// <summary>
	/// <c>@channel/list</c> opens with PennMUSH's column header (<c>src/extchat.c:2622</c>).
	/// </summary>
	[Test]
	public async ValueTask ChannelCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@channel/list"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextContains(msg, "Channels"), sender: executor, type: INotifyService.NotificationType.Announce)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async ValueTask CemitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@cemit {TestChannelName}=CemitCommand: Test message"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextEquals(msg, "CemitCommand: Test message"), sender: executor, type: INotifyService.NotificationType.Emit)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async ValueTask CemitNoisyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@cemit/noisy {TestChannelName}=CemitNoisyCommand: Test message"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"<{TestChannelName}> CemitNoisyCommand: Test message"), sender: executor, type: INotifyService.NotificationType.Emit)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async ValueTask NscemitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@nscemit {TestChannelName}=NscemitCommand: Test message"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
				TestHelpers.MessagePlainTextEquals(msg, "NscemitCommand: Test message"), sender: executor, type: INotifyService.NotificationType.NSEmit)).IsGreaterThanOrEqualTo(1);
	}

	/// <summary>
	/// NAME and RENAME are the same operation, as in PennMUSH — <c>src/extchat.c:3605-3608</c> sends
	/// both switches to <c>do_chan_admin</c> with <c>CH_ADMIN_RENAME</c>. SharpMUSH declared NAME in
	/// <c>@CHANNEL</c>'s switch list but had no arm for it, so <c>@channel/name</c> fell through to the
	/// "What do you want to do with the channel?" usage line while sharpchat.md documented it as working.
	/// </summary>
	[Test]
	[Arguments("name")]
	[Arguments("rename")]
	public async ValueTask ChannelRenameAcceptsBothSwitchSpellings(string switchName)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var suffix = Random.Shared.Next(100000, 999999);
		var before = $"Ren{switchName}{suffix}";
		var after = $"Post{switchName}{suffix}";

		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(before), TestChannelPrivileges, _testPlayer!));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@channel/{switchName} {before}={after}"));

		await Assert.That(NotifyService.Recorded().Count(executor, msg =>
					TestHelpers.MessagePlainTextEquals(msg, "CHAT: Renamed channel."), sender: executor, type: INotifyService.NotificationType.Announce)).IsGreaterThanOrEqualTo(1);

		await Assert.That(await Mediator.Send(new GetChannelQuery(after))).IsNotNull();
	}
}
