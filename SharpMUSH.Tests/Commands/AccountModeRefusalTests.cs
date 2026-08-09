using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The MAKE and PLAY socket commands require AccountMode, and there are two ways to not be in it:
/// never having authenticated, and having already bound a character. Exact-match SOCKET commands are
/// dispatched for ANY handle (see <c>SharpMUSHParserVisitor</c>'s socket block — only the abbreviation
/// path is pre-login-only), so a player in the game who types <c>play &lt;other&gt;</c> reaches these
/// commands and must be told the truth: telnet is one session per character, and switching means
/// reconnecting. Telling them to log in is false — they are logged in, which is the whole problem.
/// </summary>
public class AccountModeRefusalTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const string LoginPrompt = "You must be logged in to an account first.";
	private const string ReconnectAdvice = "To play a different one, disconnect and connect again.";

	private async Task RegisterAsync(long handle) =>
		await ConnectionService.Register(handle, "127.0.0.1", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);

	/// <summary>
	/// Drops the handle again. <c>IConnectionService</c> is a session-wide singleton, so a handle left
	/// registered here stays in the connection list every later test reads — WHO and DOING would count and
	/// render this test's fake sessions and fail on the extra rows.
	/// </summary>
	private async Task DisconnectAsync(long handle) => await ConnectionService.Disconnect(handle);

	private async Task<bool> SawAsync(long handle, string fragment)
	{
		var received = NotifyService.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Select(c => c.GetArguments())
			.Where(a => a.Length > 1 && a[0] is long h && h == handle)
			.Select(a => a[1])
			.OfType<OneOf<MString, string>>()
			.Any(m => TestHelpers.MessagePlainTextContains(m, fragment));

		return await Task.FromResult(received);
	}

	[Test]
	public async ValueTask Play_WhenNeverAuthenticated_SaysToLogIn()
	{
		const long handle = 24601L;
		await RegisterAsync(handle);
		try
		{
			await Parser.CommandParse(handle, ConnectionService, MModule.single("play Someone"));

			await Assert.That(await SawAsync(handle, LoginPrompt)).IsTrue()
				.Because("a connection that never authenticated genuinely does need to log in first");
		}
		finally
		{
			await DisconnectAsync(handle);
		}
	}

	[Test]
	public async ValueTask Play_WhenAlreadyPlayingACharacter_SaysReconnectInstead()
	{
		const long handle = 24602L;
		await RegisterAsync(handle);
		try
		{
			await ConnectionService.BindAccount(handle, "accounts/refusal-play");
			await ConnectionService.Bind(handle, new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

			await Parser.CommandParse(handle, ConnectionService, MModule.single("play Someone"));

			await Assert.That(await SawAsync(handle, ReconnectAdvice)).IsTrue()
				.Because("switching characters on telnet means reconnecting — that is the actual rule");
			await Assert.That(await SawAsync(handle, LoginPrompt)).IsFalse()
				.Because("this connection IS logged in; telling it to log in is the misleading refusal");
		}
		finally
		{
			await DisconnectAsync(handle);
		}
	}

	[Test]
	public async ValueTask Make_WhenAlreadyPlayingACharacter_SaysReconnectInstead()
	{
		const long handle = 24603L;
		await RegisterAsync(handle);
		try
		{
			await ConnectionService.BindAccount(handle, "accounts/refusal-make");
			await ConnectionService.Bind(handle, new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

			await Parser.CommandParse(handle, ConnectionService, MModule.single("make Newbie secretpassword"));

			await Assert.That(await SawAsync(handle, ReconnectAdvice)).IsTrue();
			await Assert.That(await SawAsync(handle, LoginPrompt)).IsFalse();
		}
		finally
		{
			await DisconnectAsync(handle);
		}
	}
}
