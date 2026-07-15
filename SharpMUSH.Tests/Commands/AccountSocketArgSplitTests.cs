using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Regression coverage for the telnet REGISTER/LOGIN/MAKE argument-splitting bug: these
/// commands are <c>CommandBehavior.SOCKET | CommandBehavior.NoParse</c>, so the parser never
/// populates <c>Arguments["1"]</c>/<c>["2"]</c> — the entire remainder of the line after the
/// command word lands in <c>Arguments["0"]</c> alone. The previous implementation read
/// positional args <c>["0"]</c>/<c>["1"]</c>/<c>["2"]</c> directly, so any multi-word input
/// (which is the *only* valid input — these commands always take at least two words) fell
/// through to the "Usage:" branch. These tests drive the real socket commands end-to-end
/// through <c>Parser.CommandParse</c> against the real DB and would have failed with a
/// "Usage: ..." notification before the whitespace-split fix.
/// </summary>
public class AccountSocketArgSplitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAccountService AccountService => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>
	/// Registers a real connection (as <see cref="PlayerCreationConfigTests"/> does) so that
	/// <see cref="IConnectionService.BindAccount"/>/<see cref="IConnectionService.Bind"/> —
	/// both no-ops against an unregistered handle — actually take effect and the resulting
	/// <see cref="IConnectionService.ConnectionState"/> can be asserted on.
	/// </summary>
	private async ValueTask<long> RegisterConnectionAsync(long handle)
	{
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	[Test, NotInParallel(nameof(AccountSocketArgSplitTests))]
	public async ValueTask Register_TwoTokens_CreatesAccount()
	{
		var username = TestIsolationHelpers.GenerateUniqueName("regtwo");
		var handle = await RegisterConnectionAsync(4001L);

		await Parser.CommandParse(handle, ConnectionService, MModule.single($"register {username} some-password-1"));

		var account = await AccountService.GetByUsernameAsync(username);
		await Assert.That(account).IsNotNull();
		await Assert.That(account!.Email).IsNull();

		var state = ConnectionService.Get(handle)?.State;
		await Assert.That(state).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test, NotInParallel(nameof(AccountSocketArgSplitTests))]
	public async ValueTask Register_ThreeTokens_CreatesAccountWithEmail()
	{
		var username = TestIsolationHelpers.GenerateUniqueName("regthree");
		var email = $"{username}@example.test";
		var handle = await RegisterConnectionAsync(4002L);

		await Parser.CommandParse(handle, ConnectionService, MModule.single($"register {username} {email} some-password-1"));

		var account = await AccountService.GetByUsernameAsync(username);
		await Assert.That(account).IsNotNull();
		await Assert.That(account!.Email).IsEqualTo(email);

		var state = ConnectionService.Get(handle)?.State;
		await Assert.That(state).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test, NotInParallel(nameof(AccountSocketArgSplitTests))]
	public async ValueTask Register_OneToken_ShowsUsageError()
	{
		var handle = await RegisterConnectionAsync(4003L);

		await Parser.CommandParse(handle, ConnectionService, MModule.single("register soloname"));

		await NotifyService.Received(1).Notify(
			Arg.Is<long>(h => h == handle),
			Arg.Is<OneOf<MString, string>>(s =>
				TestHelpers.MessagePlainTextEquals(s, "Usage: register <username> [email] <password>")),
			null, INotifyService.NotificationType.Announce);

		var state = ConnectionService.Get(handle)?.State;
		await Assert.That(state).IsNotEqualTo(IConnectionService.ConnectionState.AccountMode);
	}

	[Test, NotInParallel(nameof(AccountSocketArgSplitTests))]
	public async ValueTask Login_AfterRegister_Succeeds()
	{
		var username = TestIsolationHelpers.GenerateUniqueName("loginok");
		var registerHandle = await RegisterConnectionAsync(4004L);
		await Parser.CommandParse(registerHandle, ConnectionService, MModule.single($"register {username} some-password-1"));

		var loginHandle = await RegisterConnectionAsync(4005L);
		await Parser.CommandParse(loginHandle, ConnectionService, MModule.single($"login {username} some-password-1"));

		var state = ConnectionService.Get(loginHandle)?.State;
		await Assert.That(state).IsEqualTo(IConnectionService.ConnectionState.AccountMode);
	}
}
