using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A command line may start with spaces; the command is matched on its trimmed text, and the handlers
/// that slice the raw line themselves must skip the same spaces before splitting the command token off.
/// </summary>
public class IndentedCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreatePlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<IReadOnlyList<string>> NotifiedWhile(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before)];
	}

	[Test]
	[Arguments("")]
	[Arguments("  ")]
	public async Task StandardAttributeCommand_SetsTheAttribute(string indent)
	{
		var player = await CreatePlayerAsync("IndentDesc");
		var description = TestIsolationHelpers.GenerateUniqueName("Desc");

		await NotifiedWhile(player, $"{indent}@describe me={description}");

		var stored = await WebAppFactoryArg.CommandParser.FunctionParse(
			MarkupText.Plain($"get(#{player.DbRef.Number}/DESCRIBE)"));
		await Assert.That(stored!.Message.ToPlainText()).IsEqualTo(description);
	}

	[Test]
	[Arguments("")]
	[Arguments("  ")]
	public async Task ChannelAlias_ChatsOnlyTheMessage(string indent)
	{
		var player = await CreatePlayerAsync("IndentChat");
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var channelName = TestIsolationHelpers.GenerateUniqueName("IC");
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(channelName), ["Open", "Player"], owner));
		try
		{
			var message = TestIsolationHelpers.GenerateUniqueName("Hi");

			var heard = await NotifiedWhile(player, $"{indent}+{channelName} {message}");

			await Assert.That(heard.Any(line => line.Contains($"\"{message}\""))).IsTrue()
				.Because($"the channel carried: {string.Join(" | ", heard)}");
			await Assert.That(heard.Any(line => line.Contains($"+{channelName}"))).IsFalse()
				.Because($"the command token is not part of the message: {string.Join(" | ", heard)}");
		}
		finally
		{
			if (await Mediator.Send(new GetChannelQuery(channelName)) is { } channel)
			{
				await Mediator.Send(new DeleteChannelCommand(channel));
			}
		}
	}
}
