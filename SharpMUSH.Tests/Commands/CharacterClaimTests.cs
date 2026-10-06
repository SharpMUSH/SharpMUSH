using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A character made apart from any account (<c>create</c>, <c>@pcreate</c>, an import) reaches one later:
/// its holder claims it from the account menu with its password, or staff link it with
/// <c>@account/link</c>.
/// </summary>
public class CharacterClaimTests : ServerTestBase
{
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	private async Task<SharpAccount> NewAccountAsync() =>
		(await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("claimacct"), null, "claim-password-1"))
		.Expect<SharpAccount>();

	private async Task<SharpPlayer> NewCharacterAsync() =>
		(await Mediator.Send(new GetObjectNodeQuery(
			await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "Claimed"))))
		.Expect<SharpPlayer>();

	/// <summary>A connection at the account menu, logged in to <paramref name="account"/>.</summary>
	private async Task<long> AccountMenuAsync(SharpAccount account)
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		await ConnectionService.BindAccount(handle, account.Id!);
		return handle;
	}

	private async Task<string?> HolderAsync(SharpPlayer character) =>
		(await Accounts.GetAccountForCharacterAsync(character.Object.DBRef))?.Id;

	private bool Saw(long handle, string fragment) =>
		Notifications.ForHandle(handle).Any(message => message.Contains(fragment, StringComparison.Ordinal));

	[Test]
	public async Task Claim_WithTheCharactersPassword_LinksIt()
	{
		var account = await NewAccountAsync();
		var character = await NewCharacterAsync();
		var handle = await AccountMenuAsync(account);
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService,
				MString.Plain($"claim {character.Object.Name} {TestIsolationHelpers.TestPassword}"));

			await Assert.That(await HolderAsync(character)).IsEqualTo(account.Id);
			await Assert.That(Saw(handle, $"{character.Object.Name} is now linked to your account.")).IsTrue();
			await Assert.That((await Accounts.GetCharactersAsync(account.Id!)).Select(c => c.Object.Key))
				.Contains(character.Object.Key);
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task Claim_WithAWrongPassword_LinksNothing()
	{
		var account = await NewAccountAsync();
		var character = await NewCharacterAsync();
		var handle = await AccountMenuAsync(account);
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService,
				MString.Plain($"claim {character.Object.Name} not-the-password"));

			await Assert.That(await HolderAsync(character)).IsNull();
			await Assert.That(Saw(handle, "No character has that name and password.")).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task Claim_OfACharacterOnAnotherAccount_IsRefused()
	{
		var holder = await NewAccountAsync();
		var claimant = await NewAccountAsync();
		var character = await NewCharacterAsync();
		await Accounts.LinkCharacterAsync(holder.Id!, character.Object.DBRef);
		var handle = await AccountMenuAsync(claimant);
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService,
				MString.Plain($"claim {character.Object.Name} {TestIsolationHelpers.TestPassword}"));

			await Assert.That(await HolderAsync(character)).IsEqualTo(holder.Id);
			await Assert.That(Saw(handle, "linked to another account")).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	/// <summary>Nothing proves who owns a character with no password, so only staff link one.</summary>
	[Test]
	public async Task Claim_OfACharacterWithNoPassword_IsRefused()
	{
		var account = await NewAccountAsync();
		var home = new DBRef(0);
		var name = TestIsolationHelpers.GenerateUniqueName("NoPass");
		var character = (await Mediator.Send(new GetObjectNodeQuery(
			await Mediator.Send(new CreatePlayerCommand(name, string.Empty, home, home, 0, Salt: string.Empty)))))
			.Expect<SharpPlayer>();

		(await Accounts.ClaimCharacterAsync(account.Id!, name, "anything")).Expect<NotFound>();
		(await Accounts.ClaimCharacterAsync(account.Id!, name, string.Empty)).Expect<NotFound>();
		await Assert.That(await HolderAsync(character)).IsNull();
	}

	[Test]
	public async Task Claim_BeforeLoggingIn_SaysToLogIn()
	{
		var character = await NewCharacterAsync();
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService,
				MString.Plain($"claim {character.Object.Name} {TestIsolationHelpers.TestPassword}"));

			await Assert.That(await HolderAsync(character)).IsNull();
			await Assert.That(Saw(handle, "You must be logged in to an account first.")).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	public async Task AccountLink_AttachesACharacterWithoutItsPassword()
	{
		var account = await NewAccountAsync();
		var character = await NewCharacterAsync();

		await Cmd($"@account/link {account.Username}={character.Object.Name}");

		await Assert.That(await HolderAsync(character)).IsEqualTo(account.Id);
		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef))
			.Contains($"{character.Object.Name} is now linked to account '{account.Username}'.");
	}

	[Test]
	public async Task AccountLink_OfACharacterOnAnotherAccount_IsRefused()
	{
		var holder = await NewAccountAsync();
		var other = await NewAccountAsync();
		var character = await NewCharacterAsync();
		await Accounts.LinkCharacterAsync(holder.Id!, character.Object.DBRef);

		await Cmd($"@account/link {other.Username}={character.Object.Name}");

		await Assert.That(await HolderAsync(character)).IsEqualTo(holder.Id);
		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef).Any(message =>
			message.StartsWith($"{character.Object.Name} is linked to account '{holder.Username}'.", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task AccountUnlink_TakesTheCharacterOff()
	{
		var account = await NewAccountAsync();
		var character = await NewCharacterAsync();
		await Accounts.LinkCharacterAsync(account.Id!, character.Object.DBRef);

		await Cmd($"@account/unlink {account.Username}={character.Object.Name}");

		await Assert.That(await HolderAsync(character)).IsNull();
		await Assert.That(Notifications.For(WebAppFactoryArg.ExecutorDBRef))
			.Contains($"{character.Object.Name} is no longer linked to account '{account.Username}'.");
	}
}
