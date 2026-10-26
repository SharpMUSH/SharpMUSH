using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MiscCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private OwnRoomPlayers Players => new(WebAppFactoryArg);
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask GrepCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep #1=pattern"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask GrepCommand_WithPrintSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep/print #1=pattern"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask GrepCommand_WithWildSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep/wild #1=*pattern*"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask GrepCommand_WithRegexpSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep/regexp #1=.*pattern.*"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask GrepCommand_WithNocaseSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep/nocase #1=PATTERN"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask GrepCommand_WithAttributePattern()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@grep #1/DESC*=pattern"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor, executor)).IsTrue();
	}

	/// <summary>PennMUSH <c>cmd_session</c> (<c>cmds.c:1810-1816</c>): a mortal gets the mortal WHO.</summary>
	[Test]
	public async ValueTask SessionCommand()
	{
		var player = await Players.PlayerAsync("SessionCmd");

		var lines = (await Players.HeardRunning(player, "session")).Single().Split('\n');

		await Assert.That(lines[0]).StartsWith("Player Name");
		await Assert.That(lines.Any(line => line.StartsWith(player.Name, StringComparison.Ordinal))).IsTrue();
	}

	/// <summary>Once logged in, <c>connect</c> is not a command, so PennMUSH answers with its Huh? message.</summary>
	[Test]
	public async ValueTask ConnectCommand()
	{
		var player = await Players.PlayerAsync("ConnectCmd");
		var before = WebAppFactoryArg.Notifications.CountForHandle(player.Handle);

		await Players.RunAsync(player, "connect player password");

		await Assert.That(WebAppFactoryArg.Notifications.ForHandle(player.Handle).Skip(before))
			.IsEquivalentTo(["Huh?  (Type \"help\" for help.)"]);
	}

	/// <summary>@prompt and @nsprompt send the message to the target as a prompt and nothing else.</summary>
	[Test]
	[Arguments("@prompt")]
	[Arguments("@nsprompt")]
	public async ValueTask PromptCommand(string command)
	{
		var room = await Players.DigRoomAsync("PromptRoom");
		var sender = await Players.PlayerAsync("PromptSender", room);
		var target = await Players.PlayerAsync("PromptTarget", room);
		var token = TestIsolationHelpers.GenerateUniqueName("Prompt");
		var promptsBefore = WebAppFactoryArg.Notifications.PromptCountFor(target.DbRef);

		var heard = await Players.HeardWhile(target, () => Players.RunAsync(sender, $"{command}/silent {target.Name}={token}"));

		await Assert.That(WebAppFactoryArg.Notifications.PromptsFor(target.DbRef).Skip(promptsBefore)).IsEquivalentTo([token]);
		await Assert.That(heard).IsEmpty();
	}
}
