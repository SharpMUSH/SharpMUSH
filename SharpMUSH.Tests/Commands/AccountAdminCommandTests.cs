using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Extensions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AccountAdminCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_actor!.DbRef, _actor.Handle);
	private TestIsolationHelpers.TestPlayer? _actor;
	private readonly string _username = TestIsolationHelpers.GenerateUniqueName("account");
	private readonly string _email = $"{Guid.NewGuid():N}@example.com";

	[Before(Test)]
	public async Task CreateAdministrator()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, mediator, ConnectionService, "AccountAdmin");
		var player = (await mediator.Send(new GetObjectNodeQuery(_actor.DbRef))).AsPlayer;
		var wizard = await mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await mediator.Send(new SetObjectFlagCommand(player, wizard!))).IsTrue();
	}

	[After(Test)]
	public async Task DisconnectAdministrator()
	{
		if (_actor is not null) await ConnectionService.Disconnect(_actor.Handle);
	}

	[Test]
	public async ValueTask AccountNewPassword_SetsPasswordAndFlag()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var accountSessionStore = WebAppFactoryArg.Services.GetRequiredService<IAccountSessionStore>();
		var createResult = await accountService.CreateAccountAsync(_username, null, "old-password-1");
		var accountId = createResult.Expect<SharpAccount>().Id!;
		var sessionToken = await accountSessionStore.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15), "0.0.0.0");

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/newpassword {_username}=temp-password-9"));

		var authenticated = await accountService.AuthenticateAsync(_username, "temp-password-9");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsTrue();

		// The old session must be revoked as part of the password reset.
		await Assert.That(await accountSessionStore.ValidateAsync(sessionToken)).IsNull();
	}

	[Test]
	public async ValueTask AccountDisable_BlocksLogin_EnableRestores()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync(_username, null, "some-password-1");

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/disable {_username}"));
		await Assert.That(await accountService.AuthenticateAsync(_username, "some-password-1")).IsNull();

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/enable {_username}"));
		await Assert.That(await accountService.AuthenticateAsync(_username, "some-password-1")).IsNotNull();
	}

	[Test]
	public async ValueTask AccountClose_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync(_username, _email, "some-password-1");

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/close {_username}"));

		await Assert.That(await accountService.AuthenticateAsync(_username, "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync(_username);
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Closed);
		await Assert.That(reloaded.Email).IsEqualTo(_email);
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test]
	public async ValueTask AccountDelete_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync(_username, _email, "some-password-1");

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/delete {_username}"));

		await Assert.That(await accountService.AuthenticateAsync(_username, "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync(_username);
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Deleted);
		await Assert.That(reloaded.Email).IsEqualTo(_email);
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test]
	public async ValueTask AccountClose_SystemAccount_IsRefused()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var system = await accountService.GetOrCreateSystemAccountAsync();

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/close {SystemAccount.Username}"));

		var reloaded = await accountService.GetByIdAsync(system.Id!);
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Active);
	}

	[Test]
	public async ValueTask AccountNewPassword_TooShort_RefusesAndLeavesPasswordUnchanged()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync(_username, null, "old-password-1");

		await Parser.CommandParse(_actor!.Handle, ConnectionService, MarkupText.Plain($"@account/newpassword {_username}=short"));

		// The refusal must not change the password.
		var authenticated = await accountService.AuthenticateAsync(_username, "old-password-1");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsFalse();

		await Assert.That(await accountService.AuthenticateAsync(_username, "short")).IsNull();
	}
}
