using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Regression coverage for PennMUSH-style unambiguous prefix abbreviation of pre-login
/// SOCKET commands (e.g. "con"/"co"/"conn" -&gt; CONNECT). Before the fix in
/// <see cref="SharpMUSH.Implementation.Visitors.SharpMUSHParserVisitor"/>, pre-login socket
/// commands were dispatched only by exact case-insensitive name match, so any abbreviation
/// (e.g. typing "con god pass") fell through to "No such command available at login." even
/// though the abbreviation is unambiguous (CONNECT is the only registered SOCKET command
/// starting with "C" among WHO/CONNECT/QUIT/REGISTER/LOGIN/MAKE/PLAY).
///
/// These tests drive the real dispatch path in
/// <c>SharpMUSHParserVisitor.EvaluateCommands</c> through <c>Parser.CommandParse</c> against
/// the real database, mirroring <see cref="GuestLoginTests"/> and
/// <see cref="AccountSocketArgSplitTests"/>.
/// </summary>
public class SocketCommandAbbreviationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	/// <summary>
	/// Registers a real connection (as <see cref="AccountSocketArgSplitTests"/> does) so that
	/// <see cref="IConnectionService.Bind"/> — a no-op against an unregistered handle — actually
	/// takes effect and <see cref="IConnectionService.ConnectionState"/>/<c>Ref</c> can be
	/// asserted on afterwards.
	/// </summary>
	private async ValueTask<long> RegisterConnectionAsync(long handle)
	{
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	private async ValueTask<string> CreateTestPlayerAsync(string name, string password)
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		await Mediator.Send(new CreatePlayerCommand(name, password, defaultHome, defaultHome, startingQuota));
		return name;
	}

	private static string PlainMessage(CallState result) => result.Message?.ToString() ?? "";

	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask Connect_FullCommand_Succeeds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrfull");
		await CreateTestPlayerAsync(name, "correct-pass-1");
		var handle = await RegisterConnectionAsync(6001L);

		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"connect {name} correct-pass-1"));

		await Assert.That(PlainMessage(result).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}

	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask Connect_ConAbbreviation_Succeeds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrcon");
		await CreateTestPlayerAsync(name, "correct-pass-2");
		var handle = await RegisterConnectionAsync(6002L);

		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"con {name} correct-pass-2"));

		await Assert.That(PlainMessage(result).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}

	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask Connect_CoAbbreviation_Succeeds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrco");
		await CreateTestPlayerAsync(name, "correct-pass-3");
		var handle = await RegisterConnectionAsync(6003L);

		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"co {name} correct-pass-3"));

		await Assert.That(PlainMessage(result).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}

	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask Connect_ConnAbbreviation_Succeeds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrconn");
		await CreateTestPlayerAsync(name, "correct-pass-4");
		var handle = await RegisterConnectionAsync(6004L);

		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"conn {name} correct-pass-4"));

		await Assert.That(PlainMessage(result).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}

	/// <summary>
	/// Proves that the abbreviation dispatch doesn't mangle the argument remainder: a wrong
	/// password through "con" must fail auth exactly like a wrong password through "connect"
	/// would, and the right password through "con" must still succeed.
	/// </summary>
	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask Connect_AbbreviationPassword_WrongFailsRightSucceeds()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrpass");
		await CreateTestPlayerAsync(name, "the-real-password-9");

		var wrongHandle = await RegisterConnectionAsync(6005L);
		var wrongResult = await Parser.CommandParse(wrongHandle, ConnectionService, MModule.single($"con {name} not-the-password"));
		await Assert.That(PlainMessage(wrongResult)).IsEqualTo(ErrorMessages.Returns.InvalidPassword);
		await Assert.That(ConnectionService.Get(wrongHandle)?.Ref).IsNull();

		var rightHandle = await RegisterConnectionAsync(6006L);
		var rightResult = await Parser.CommandParse(rightHandle, ConnectionService, MModule.single($"con {name} the-real-password-9"));
		await Assert.That(PlainMessage(rightResult).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(rightHandle)?.Ref).IsNotNull();
	}

	/// <summary>
	/// A nonsense token matches zero registered SOCKET commands (not an ambiguous prefix —
	/// among WHO/CONNECT/QUIT/REGISTER/LOGIN/MAKE/PLAY no two commands share a first letter, so
	/// there is no real ambiguous-prefix case to exercise here). This must still fall through to
	/// the existing "no such command at login" behavior, unchanged.
	/// </summary>
	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask UnknownAbbreviation_DoesNotConnect_NoSuchCommandAtLogin()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrxyz");
		await CreateTestPlayerAsync(name, "irrelevant-password");
		var handle = await RegisterConnectionAsync(6007L);

		await Parser.CommandParse(handle, ConnectionService, MModule.single($"xyz {name} irrelevant-password"));

		var gotNoSuchCommandNotice = NotifyService.ReceivedCalls().Any(c =>
			c.GetMethodInfo().Name == "NotifyLocalized" &&
			c.GetArguments().Length >= 2 &&
			c.GetArguments()[0] is long h && h == handle &&
			c.GetArguments()[1] is string k && k == nameof(ErrorMessages.Notifications.NoSuchCommandAtLogin));

		await Assert.That(gotNoSuchCommandNotice).IsTrue();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
	}

	/// <summary>
	/// Post-login regression: once a connection is bound to a player, <c>CommandParse</c> sets
	/// <c>Executor</c> to the player's DBRef (non-null). Prefix abbreviation of connect-screen
	/// SOCKET commands is a pre-login-only behavior, so a bare abbreviation like "q" from a
	/// logged-in player must NOT be prefix-matched to the SOCKET QUIT command and disconnect them.
	/// It is an ordinary in-game command and the player stays connected. Before the fix in
	/// <see cref="SharpMUSH.Implementation.Visitors.SharpMUSHParserVisitor"/> the abbreviation
	/// block was gated only on <c>Handle is not null</c> (true for the whole connection lifetime),
	/// so a logged-in player typing "q" was silently QUIT'd.
	/// </summary>
	[Test, NotInParallel(nameof(SocketCommandAbbreviationTests))]
	public async ValueTask PostLogin_BareAbbreviation_DoesNotDispatchQuit()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("abbrpost");
		await CreateTestPlayerAsync(name, "post-login-pass-1");
		var handle = await RegisterConnectionAsync(6008L);

		// Log the player in: after this the connection is bound and Executor resolves to the
		// player's DBRef on subsequent commands.
		var connectResult = await Parser.CommandParse(handle, ConnectionService, MModule.single($"connect {name} post-login-pass-1"));
		await Assert.That(PlainMessage(connectResult).Contains("#-1")).IsFalse();
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();

		// A bare "q" from a logged-in player must not hijack to SOCKET QUIT (which would
		// Disconnect the handle and clear its Ref). "q" uniquely prefixes QUIT among the SOCKET
		// commands, so an unrestricted abbreviation block would definitely dispatch it.
		await Parser.CommandParse(handle, ConnectionService, MModule.single("q"));

		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull();
	}
}
