using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What a login tells the player after the MOTD, as PennMUSH's <c>check_connect</c> does: the refusals of a
/// wrong name or password, the last connect and the last failed one, and the mail report.
/// </summary>
public class LoginReportTests : ServerTestBase
{
	private const string Password = "TestPassword123";

	private async Task<long> RegisterAsync(string host)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(handle, "198.51.100.20", host, "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	private async Task LogInAsync(long handle, string name, string password) =>
		await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect \"{name}\" {password}"));

	private async Task<(DBRef Player, string Name)> PlayerAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, prefix);
		return (player, (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<SharpPlayer>().Object.Name);
	}

	[Test]
	public async Task AnUnknownName_IsToldThereIsNoSuchPlayer()
	{
		var handle = await RegisterAsync("login.example");
		await LogInAsync(handle, TestIsolationHelpers.GenerateUniqueName("NoOne"), "whatever");
		try
		{
			await Assert.That(Notifications.ForHandle(handle)).Contains("There is no player with that name.");
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task AWrongPassword_IsRefused_AndTheNextLoginIsToldWhereItCameFrom()
	{
		var (player, name) = await PlayerAsync("FailedLogin");
		var first = await RegisterAsync("login.example");
		await LogInAsync(first, name, Password);
		var refused = await RegisterAsync("bad.example");
		await LogInAsync(refused, name, "wrong");
		var second = 0L;
		try
		{
			await Assert.That(Notifications.ForHandle(refused)).Contains("That is not the correct password.");
			await Assert.That(ConnectionService.Get(refused)?.Ref).IsNull();

			var before = Notifications.CountFor(player);
			second = await RegisterAsync("login.example");
			await LogInAsync(second, name, Password);
			var heard = Notifications.For(player).Skip(before).ToList();
			await Assert.That(heard).Contains(line => line.StartsWith("Last FAILED connect was from bad.example on ", StringComparison.Ordinal));
		}
		finally
		{
			await ConnectionService.Disconnect(first);
			await ConnectionService.Disconnect(refused);
			if (second != 0) await ConnectionService.Disconnect(second);
		}
	}

	[Test]
	public async Task ALoginWithAnEmptyMailbox_IsToldThereIsNoMail()
	{
		var (player, name) = await PlayerAsync("NoMailLogin");
		var handle = await RegisterAsync("login.example");
		await LogInAsync(handle, name, Password);
		try
		{
			await Assert.That(Notifications.For(player)).Contains("\nMAIL: You have no mail.");
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task ALoginWithMail_IsToldWhatEachFolderHolds()
	{
		var (player, name) = await PlayerAsync("MailLogin");
		await Cmd($"@mail #{player.Number}=Waiting/Read me when you arrive.");
		var handle = await RegisterAsync("login.example");
		await LogInAsync(handle, name, Password);
		try
		{
			var heard = Notifications.For(player);
			await Assert.That(heard).Contains("MAIL: 1 messages in folder 0 [INBOX] (1 unread, 0 cleared).");
			await Assert.That(heard).DoesNotContain("\nMAIL: You have no mail.");
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task APlayerMadeWithPcreate_IsToldOfItsCreationAtItsFirstLogin()
	{
		// A player name is at most 16 characters, shorter than GenerateUniqueName's.
		var name = $"Made_{Guid.NewGuid().ToString("N")[..8]}";
		await Cmd($"@pcreate {name}={Password}");
		var player = (await Mediator.CreateStream(new GetPlayerQuery(name)).FirstAsync()).Object.DBRef;
		var handle = await RegisterAsync("login.example");
		await LogInAsync(handle, name, Password);
		try
		{
			await Assert.That(Notifications.For(player))
				.Contains(line => line.StartsWith("Last connect was from None on ", StringComparison.Ordinal));
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task ACharacterMadeAtTheConnectScreen_IsToldItsCreationSiteAtFirstLogin()
	{
		var handle = await RegisterAsync("maker.example");
		try
		{
			var account = await WebAppFactoryArg.Services.GetRequiredService<IAccountService>()
				.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("MakerAcct"), null, Password);
			await ConnectionService.BindAccount(handle, account.Expect<SharpAccount>().Id!);
			var name = $"Maker{Guid.NewGuid():N}"[..12];

			await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"create {name} {Password}"));

			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
			var player = ConnectionService.Get(handle)?.Ref ?? default;
			await Assert.That(Notifications.For(player))
				.Contains(line => line.StartsWith("Last connect was from maker.example on ", StringComparison.Ordinal));
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task ALoginWhoseHostIsNamedBeforeItBinds_IsRefusedByAConnectRuleForThatName()
	{
		var (_, name) = await PlayerAsync("LockedLogin");
		var host = $"{TestIsolationHelpers.GenerateUniqueName("locked")}.example".ToLowerInvariant();
		const string address = "198.51.100.20";
		var handle = await RegisterAsync(address);
		// The host-name lookup finishing after the connect screen's sitelock check and before the bind.
		ConnectionService.ListenState(change =>
		{
			if (change.Item1 == handle && change.Item4 == IConnectionService.ConnectionState.LoggedIn)
				ConnectionService.Update(handle, "HostName", host);
		});
		try
		{
			using (TestOptionsOverride.Scope(options => options with
			{
				SitelockRules = new SitelockRulesOptions(
					new Dictionary<string, string[]>(options.SitelockRules.Rules) { [host] = ["!connect"] })
			}))
			{
				await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect \"{name}\" {Password}"));
			}

			await Assert.That(Notifications.ForHandle(handle)).Contains("Access from your location is restricted.");
			await Assert.That(ConnectionService.Get(handle)).IsNull();
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}
}
