using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>connect &lt;character&gt; &lt;password&gt;</c> takes the password of the account that holds the character as well
/// as the character's own, so a character made on the portal without a password of its own can connect on telnet.
/// </summary>
public class AccountPasswordConnectTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IPasswordService Passwords => WebAppFactoryArg.Services.GetRequiredService<IPasswordService>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	/// <summary>An account with <paramref name="accountPassword"/> holding a new character, made as the portal makes one.</summary>
	private async ValueTask<(string Character, SharpAccount Account)> CharacterOnAccountAsync(string accountPassword,
		string? characterPassword = null)
	{
		var character = TestIsolationHelpers.GenerateUniqueName("acctconn");
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("acctconnacct"), null,
			accountPassword)).Expect<SharpAccount>();
		var home = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var quota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var dbref = characterPassword is null
			? await Mediator.Send(new CreatePlayerCommand(character, Passwords.HashPassword(Passwords.GenerateRandomPassword()),
				home, home, quota, Salt: ""))
			: await Mediator.Send(new CreatePlayerCommand(character, characterPassword, home, home, quota));
		await Accounts.LinkCharacterAsync(account.Id!, dbref);
		return (character, account);
	}

	private async ValueTask<(CallState Result, long Handle)> ConnectAsync(string character, string password)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect {character} {password}")), handle);
	}

	private async ValueTask ConnectsAsync(string character, string password)
	{
		var (result, handle) = await ConnectAsync(character, password);
		await Assert.That(result.Message.ToPlainText()).DoesNotStartWith("#-1");
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}

	private async ValueTask RefusedAsync(string character, string password)
	{
		var (result, handle) = await ConnectAsync(character, password);
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidPassword);
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
	}

	[Test]
	public async ValueTask PortalCharacterConnectsWithItsAccountPassword()
	{
		var (character, _) = await CharacterOnAccountAsync("account-secret-1");

		await RefusedAsync(character, "not-the-account-secret");
		await ConnectsAsync(character, "account-secret-1");
	}

	[Test]
	public async ValueTask CharacterWithItsOwnPasswordTakesEither()
	{
		var (character, _) = await CharacterOnAccountAsync("account-secret-2", characterPassword: "character-secret-2");

		await ConnectsAsync(character, "character-secret-2");
		await ConnectsAsync(character, "account-secret-2");
	}

	[Test]
	public async ValueTask AnotherAccountsPasswordDoesNotConnect()
	{
		var (character, _) = await CharacterOnAccountAsync("account-secret-3");
		await CharacterOnAccountAsync("other-account-secret-3");

		await RefusedAsync(character, "other-account-secret-3");
	}

	[Test]
	public async ValueTask DeletedAccountsPasswordDoesNotConnect()
	{
		var (character, account) = await CharacterOnAccountAsync("account-secret-4");
		await Accounts.MarkAccountDeletedAsync(account.Id!);

		await RefusedAsync(character, "account-secret-4");
	}
}
