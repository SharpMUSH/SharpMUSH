using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Fakes the endpoints <see cref="AccountAuthService"/> touches during a switch: account-login (to
/// seed a real, logged-in service — its members aren't virtual, so it can't be NSubstitute-faked,
/// same reason every other switch-flow test in this repo builds a real instance), switch-character
/// (the OTT mint under test — <see cref="MintOtt"/> null means "session expired", mirroring the
/// server's behavior on a stale/invalid session), and debug-ott (to observe
/// <see cref="AccountAuthService.InvalidateDebugOtt"/> — a cached debug OTT is a single-flight task,
/// so a second real HTTP call only happens if the cache was actually dropped).
/// </summary>
internal sealed class SwitchServiceApiHandler : HttpMessageHandler
{
	public string? MintOtt { get; set; } = "new-character-ott";
	public int DebugOttRequestCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1",
					username = "headwiz",
					characters = Array.Empty<object>(),
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = "Wizard",
					permissions = new[] { "*" },
				})
			});
		}

		if (request.Method == HttpMethod.Post && path == "api/auth/switch-character")
		{
			if (MintOtt is null)
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { ott = MintOtt, expiresIn = 300 })
			});
		}

		if (request.Method == HttpMethod.Get && path == "api/auth/debug-ott")
		{
			DebugOttRequestCount++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					token = $"debug-ott-{DebugOttRequestCount}",
					expiresIn = 300,
					playerName = "God",
					accountId = (string?)null,
					accountUsername = (string?)null,
					accountSessionToken = (string?)null,
					accountMustChangePassword = false,
				})
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Unit coverage for <see cref="CharacterSwitchService"/> — the canonical switch flow extracted out
/// of <c>MainLayout.SwitchCharacterAsync</c> so <c>MainLayout</c>, <c>CharacterPicker</c>, and the nav
/// account panel (<c>NavMenu</c>) all go through one place. No component is rendered: the service has
/// no UI surface of its own. <see cref="BunitContext"/> is used only for its <c>JSInterop</c>
/// convenience (sessionStorage fakes for the real <see cref="AccountAuthService"/>), the same reason
/// the UI-level switch tests (<c>NavMenuCharacterSwitchTests</c>, <c>NewTabCharacterTests</c>) use it.
/// </summary>
public class CharacterSwitchServiceTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public CharacterSwitchServiceTests()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
	}

	private sealed record Rig(
		AccountAuthService Auth,
		SwitchServiceApiHandler ApiHandler,
		TerminalServiceHost Terminal,
		ITerminalService TerminalFirst,
		ITerminalService TerminalSecond,
		PlayTerminalServiceHost PlayTerminal,
		IPlayTerminalService PlayTerminalFirst,
		IPlayTerminalService PlayTerminalSecond,
		CharacterSwitchService Service);

	private async Task<Rig> BuildAsync()
	{
		var handler = new SwitchServiceApiHandler();
		var apiClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var (success, error, _) = await auth.LoginAsync("headwiz", "password");
		if (!success)
			throw new InvalidOperationException($"Test setup login failed: {error}");

		var terminalFirst = Substitute.For<ITerminalService>();
		var terminalSecond = Substitute.For<ITerminalService>();
		var terminalQueue = new Queue<ITerminalService>([terminalFirst, terminalSecond]);
		var terminal = new TerminalServiceHost(() => terminalQueue.Dequeue());

		var playFirst = Substitute.For<IPlayTerminalService>();
		var playSecond = Substitute.For<IPlayTerminalService>();
		var playQueue = new Queue<IPlayTerminalService>([playFirst, playSecond]);
		var playTerminal = new PlayTerminalServiceHost(() => playQueue.Dequeue());

		var service = new CharacterSwitchService(auth, terminal, playTerminal);

		return new Rig(auth, handler, terminal, terminalFirst, terminalSecond, playTerminal, playFirst, playSecond, service);
	}

	[Test]
	public async Task SwitchAsync_returns_false_and_touches_nothing_when_the_ott_mint_fails()
	{
		var rig = await BuildAsync();
		rig.ApiHandler.MintOtt = null; // server reports a stale/expired session

		var result = await rig.Service.SwitchAsync(Beta);

		await Assert.That(result).IsFalse();
		await Assert.That(rig.Auth.ActiveCharacter).IsNull();
		await rig.TerminalFirst.DidNotReceive().DisposeAsync();
		await rig.PlayTerminalFirst.DidNotReceive().DisposeAsync();
	}

	[Test]
	public async Task SwitchAsync_commits_identity_and_recreates_and_reconnects_the_command_terminal()
	{
		var rig = await BuildAsync();

		var result = await rig.Service.SwitchAsync(Beta);

		await Assert.That(result).IsTrue();
		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
		await Assert.That(rig.Auth.ActiveCharacter?.Name).IsEqualTo("Beta");
		await rig.TerminalFirst.Received(1).DisposeAsync();
		await rig.TerminalSecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "new-character-ott");
	}

	[Test]
	public async Task SwitchAsync_recreates_the_play_terminal_too_but_leaves_it_disconnected()
	{
		var rig = await BuildAsync();

		await rig.Service.SwitchAsync(Beta);

		// Recreated (so it stops reporting the OLD character's stale connection)...
		await rig.PlayTerminalFirst.Received(1).DisposeAsync();
		// ...but never reconnected: /play does not auto-reconnect (deliberate, documented limitation).
		await rig.PlayTerminalSecond.DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	public async Task SwitchAsync_captures_the_command_terminals_ServerUri_before_recreating()
	{
		var rig = await BuildAsync();
		rig.TerminalFirst.ServerUri.Returns("ws://original-host:4202/ws");
		// The post-recreate inner starts with a null ServerUri, same as the real WebSocketClientService
		// before it connects — if the capture happened AFTER RecreateAsync instead of before, this
		// substitute's default (null) would be what's threaded into ConnectWithOttAsync instead.

		await rig.Service.SwitchAsync(Beta);

		await rig.TerminalSecond.Received(1).ConnectWithOttAsync("ws://original-host:4202/ws", Arg.Any<string>());
	}

	[Test]
	public async Task SwitchAsync_uses_the_serverUriOverride_when_provided()
	{
		var rig = await BuildAsync();
		rig.TerminalFirst.ServerUri.Returns("ws://should-be-ignored:4202/ws");

		await rig.Service.SwitchAsync(Beta, serverUriOverride: "ws://picker-host:4202/ws");

		await rig.TerminalSecond.Received(1).ConnectWithOttAsync("ws://picker-host:4202/ws", Arg.Any<string>());
	}

	[Test]
	public async Task SwitchAsync_invalidates_the_cached_debug_ott()
	{
		var rig = await BuildAsync();
		await rig.Auth.GetDebugOttAsync();
		await Assert.That(rig.ApiHandler.DebugOttRequestCount).IsEqualTo(1);
		// Second call while still cached must NOT hit the network again.
		await rig.Auth.GetDebugOttAsync();
		await Assert.That(rig.ApiHandler.DebugOttRequestCount).IsEqualTo(1);

		await rig.Service.SwitchAsync(Beta);

		// The cache was dropped by the switch, so the next caller re-fetches for real.
		await rig.Auth.GetDebugOttAsync();
		await Assert.That(rig.ApiHandler.DebugOttRequestCount).IsEqualTo(2);
	}

	[Test]
	public async Task SwitchAsync_commits_identity_even_when_the_final_connect_throws_and_does_not_swallow_the_exception()
	{
		var rig = await BuildAsync();
		rig.TerminalSecond.ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>())
			.Returns(Task.FromException(new InvalidOperationException("boom")));

		try
		{
			await rig.Service.SwitchAsync(Beta);
			throw new InvalidOperationException("expected SwitchAsync to propagate the connect failure");
		}
		catch (InvalidOperationException ex) when (ex.Message == "boom")
		{
			// Expected: no try/catch inside the service — a failed auto-login surfaces as a terminal
			// error, not a silently-swallowed rollback. This is the only place that pins the
			// no-rollback contract; there is no topbar surface anymore (AccountChrome was deleted).
		}

		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
