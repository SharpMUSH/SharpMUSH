using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const string LoginPrompt = "You must be logged in to an account first.";
	private const string ReconnectAdvice = "To play a different one, disconnect and connect again.";

	/// <summary>
	/// Drops the handle again. <c>IConnectionService</c> is a session-wide singleton, so a handle left
	/// registered here stays in the connection list every later test reads — WHO and DOING would count and
	/// render this test's fake sessions and fail on the extra rows.
	/// </summary>
	private async Task DisconnectAsync(long handle) => await ConnectionService.Disconnect(handle);

	private bool Saw(long handle, string fragment)
		=> WebAppFactoryArg.Notifications.ForHandle(handle).Any(message => message.Contains(fragment, StringComparison.Ordinal));

	[Test]
	public async ValueTask Play_WhenNeverAuthenticated_SaysToLogIn()
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("play Someone"));

			await Assert.That(Saw(handle, LoginPrompt)).IsTrue()
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
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await ConnectionService.BindAccount(handle, "accounts/refusal-play");
			await ConnectionService.Bind(handle, new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("play Someone"));

			await Assert.That(Saw(handle, ReconnectAdvice)).IsTrue()
				.Because("switching characters on telnet means reconnecting — that is the actual rule");
			await Assert.That(Saw(handle, LoginPrompt)).IsFalse()
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
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await ConnectionService.BindAccount(handle, "accounts/refusal-make");
			await ConnectionService.Bind(handle, new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

			await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain("make Newbie secretpassword"));

			await Assert.That(Saw(handle, ReconnectAdvice)).IsTrue();
			await Assert.That(Saw(handle, LoginPrompt)).IsFalse();
		}
		finally
		{
			await DisconnectAsync(handle);
		}
	}
}
