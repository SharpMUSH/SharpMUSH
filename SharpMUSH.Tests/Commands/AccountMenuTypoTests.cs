using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A line the account menu does not know answers <c>Huh?</c> and that account's menu, not the logged-out
/// connect screen, so a typo after <c>login</c> or <c>register</c> does not read as being signed out.
/// </summary>
public class AccountMenuTypoTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAccountService AccountService => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask UnknownLine_AtTheAccountMenu_ShowsHuhAndTheMenu()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("menutypo");
		var account = (await AccountService.CreateAccountAsync(name, null, "menu-typo-pass-1")).Expect<SharpAccount>();
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await ConnectionService.BindAccount(handle, account.Id!);
			var before = WebAppFactoryArg.Notifications.CountForHandle(handle);

			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("lokk"));

			var messages = WebAppFactoryArg.Notifications.ForHandle(handle).Skip(before).ToList();
			await Assert.That(messages).Contains("Huh?");
			await Assert.That(messages.Any(message => message.Contains($"Logged in as {name}", StringComparison.Ordinal))).IsTrue();
			await Assert.That(messages.Any(message => message.Contains("register", StringComparison.Ordinal))).IsFalse()
				.Because("the connect screen offers register and login, which a signed-in account no longer needs");
			await Assert.That(ConnectionService.Get(handle)?.State).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}
}
