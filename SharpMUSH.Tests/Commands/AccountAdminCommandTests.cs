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
		await accountService.CreateAccountAsync("cmd-reset-user", null, "old-password-1");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/newpassword cmd-reset-user=temp-password-9"));
		await Task.Delay(200);

		var authenticated = await accountService.AuthenticateAsync("cmd-reset-user", "temp-password-9");
		await Assert.That(authenticated).IsNotNull();
		await Assert.That(authenticated!.MustChangePassword).IsTrue();
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

	[Test, NotInParallel("ServerStateTests")]
	public async ValueTask AccountSetupComplete_FlipsServerState()
	{
		var db = WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		await db.SetServerSetupCompletedAsync(false);

		await Parser.CommandParse(1, ConnectionService, MModule.single("@account/setupcomplete"));
		await Task.Delay(200);

		await Assert.That((await db.GetServerStateAsync()).SetupCompleted).IsTrue();

		// Restore so later setup-flow tests see an unclaimed game.
		await db.SetServerSetupCompletedAsync(false);
	}
}
