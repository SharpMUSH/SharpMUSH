using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
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
		var sessionToken = await accountSessionStore.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15));

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
