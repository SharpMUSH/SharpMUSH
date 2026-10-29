using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@push</c>: who may run it, and the word it outputs. Nothing here reaches a push service; sending is covered by
/// WebPushServiceTests against a stand-in.
/// </summary>
public class PushCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	/// <summary>What <c>@push</c> outputs, run by God: the line <c>think</c> prints after it, behind a marker.</summary>
	private async Task<string> OutputAsGod(string push)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("push");
		var attribute = marker.ToUpperInvariant();
		var heard = await MessagesWhile(God, async () =>
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} me={push}; think {marker}: %>"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@include me/{attribute}"));
		});
		var line = heard.Single(line => line.StartsWith(marker + ":"));
		return line[(marker.Length + 1)..].Trim();
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(bool withAccount)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "Pushed");
		if (withAccount)
		{
			var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("PushAcct"), null,
				"account-pass-1")).Expect<SharpAccount>();
			await Accounts.LinkCharacterAsync(account.Id!, player.DbRef);
		}

		return player;
	}

	[Test]
	public async Task APlayerWithNoAccountIsNone()
	{
		var player = await PlayerAsync(withAccount: false);

		await Assert.That(await OutputAsGod($"@push/always #{player.DbRef.Number}=Hello")).IsEqualTo("none");
	}

	/// <summary>A connected player who has just typed is at the game, so nothing is sent unless /always says so.</summary>
	[Test]
	public async Task APlayerAtTheGameIsAttended()
	{
		var player = await PlayerAsync(withAccount: true);

		await Assert.That(await OutputAsGod($"@push #{player.DbRef.Number}=Hello")).IsEqualTo("attended");
		await Assert.That(await OutputAsGod($"@push/always #{player.DbRef.Number}=Hello")).IsEqualTo("none");
	}

	[Test]
	public async Task APathMustBeAPortalPage()
	{
		var player = await PlayerAsync(withAccount: false);

		foreach (var path in new[] { "mail", "//evil.example.com/x", "https://evil.example.com/" })
		{
			await Assert.That(await OutputAsGod($"@push/always #{player.DbRef.Number}=Hello,,{path}"))
				.IsEqualTo("#-1 INVALID ARGUMENTS");
		}
	}

	[Test]
	public async Task WithoutNotifyPushNothingIsSent()
	{
		var mortal = await PlayerAsync(withAccount: false);
		var target = await PlayerAsync(withAccount: true);

		var heard = await MessagesWhile(mortal.DbRef, async () =>
			await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@push/always #{target.DbRef.Number}=Hello")));

		await Assert.That(heard).Contains("Permission denied. You need the notify.push permission.");
	}
}
