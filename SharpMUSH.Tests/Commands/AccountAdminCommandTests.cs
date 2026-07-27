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
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountNewPassword_SetsPasswordAndFlag()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var accountSessionStore = WebAppFactoryArg.Services.GetRequiredService<IAccountSessionStore>();
		var createResult = await accountService.CreateAccountAsync("cmd-reset-user", null, "old-password-1");
		var accountId = createResult.AsT0.Id!;
		var sessionToken = await accountSessionStore.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15), "0.0.0.0");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/newpassword cmd-reset-user=temp-password-9"));
		await Task.Delay(200);

		var authenticated = await accountService.AuthenticateAsync("cmd-reset-user", "temp-password-9");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsTrue();

		// The old session must be revoked as part of the password reset.
		await Assert.That(await accountSessionStore.ValidateAsync(sessionToken)).IsNull();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountDisable_BlocksLogin_EnableRestores()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-disable-user", null, "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/disable cmd-disable-user"));
		await Task.Delay(200);
		await Assert.That(await accountService.AuthenticateAsync("cmd-disable-user", "some-password-1")).IsNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/enable cmd-disable-user"));
		await Task.Delay(200);
		await Assert.That(await accountService.AuthenticateAsync("cmd-disable-user", "some-password-1")).IsNotNull();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountClose_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-close-user", "close@example.com", "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/close cmd-close-user"));
		await Task.Delay(200);

		await Assert.That(await accountService.AuthenticateAsync("cmd-close-user", "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync("cmd-close-user");
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Closed);
		await Assert.That(reloaded.Email).IsEqualTo("close@example.com");
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountDelete_BlocksLoginAndRetainsTheRecord()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-delete-user", "delete@example.com", "some-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/delete cmd-delete-user"));
		await Task.Delay(200);

		await Assert.That(await accountService.AuthenticateAsync("cmd-delete-user", "some-password-1")).IsNull();

		var reloaded = await accountService.GetByUsernameAsync("cmd-delete-user");
		await Assert.That(reloaded).IsNotNull();
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Deleted);
		await Assert.That(reloaded.Email).IsEqualTo("delete@example.com");
		await Assert.That(reloaded.PasswordHash).IsNotEmpty();
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountClose_SystemAccount_IsRefused()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		var system = await accountService.GetOrCreateSystemAccountAsync();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@account/close {SystemAccount.Username}"));
		await Task.Delay(200);

		var reloaded = await accountService.GetByIdAsync(system.Id!);
		await Assert.That(reloaded!.Status).IsEqualTo(AccountStatus.Active);
	}

	[Test, NotInParallel(nameof(AccountAdminCommandTests))]
	public async ValueTask AccountNewPassword_TooShort_RefusesAndLeavesPasswordUnchanged()
	{
		var accountService = WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
		await accountService.CreateAccountAsync("cmd-shortpw-user", null, "old-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/newpassword cmd-shortpw-user=short"));
		await Task.Delay(200);

		// The refusal must not change the password.
		var authenticated = await accountService.AuthenticateAsync("cmd-shortpw-user", "old-password-1");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsFalse();

		await Assert.That(await accountService.AuthenticateAsync("cmd-shortpw-user", "short")).IsNull();
	}
}
