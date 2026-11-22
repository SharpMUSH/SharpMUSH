using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>chat_token_alias talks on a channel as well as +, not in its place (PennMUSH command.c:1186).</summary>
public class ChatTokenAliasTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();

	[Test]
	public async Task PlusAndTheAliasBothTalk()
	{
		var id = TestIsolationHelpers.GenerateUniqueName("C")[^6..];
		var channel = $"Cta{id}";
		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, "CtaSpeaker");
		await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/add {channel}=player"));
		await Run(speaker, $"@channel/on {channel}");

		using var alias = TestOptionsOverride.Scope(options => options with { Chat = options.Chat with { ChatTokenAlias = '~' } });

		await Assert.That(await Run(speaker, $"+{channel} plus{id}")).Contains(line => line.Contains($"plus{id}"));
		await Assert.That(await Run(speaker, $"~{channel} tilde{id}")).Contains(line => line.Contains($"tilde{id}"));
	}

	private async Task<List<string>> Run(TestIsolationHelpers.TestPlayer player, string command)
	{
		var heard = Factory.Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();
		return Factory.Notifications.For(player.DbRef).Skip(heard).ToList();
	}
}
