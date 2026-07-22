using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Fakes the endpoints an initial terminal-login touches: account-login (to seed a real logged-in
/// AccountAuthService — its members aren't virtual, so it can't be substituted) and mush-token (the
/// OTT mint; <see cref="MintOtt"/> null means the mint failed).
/// </summary>
file sealed class TerminalLoginApiHandler : HttpMessageHandler
{
	public string? MintOtt { get; set; } = "the-ott";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1",
					username = "wiz",
					characters = Array.Empty<object>(),
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = "Wizard",
					permissions = new[] { "*" },
				})
			});

		if (request.Method == HttpMethod.Post && path == "api/auth/mush-token")
			return Task.FromResult(MintOtt is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token = MintOtt, expiresIn = 60 }) });

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Coverage for <see cref="TerminalLoginService"/> — the initial terminal-connect-as-character path
/// (picker, single-char auto-login, <c>?as=</c> new tab). It mints the OTT, commits the active
/// character, and opens the terminal socket. It is NOT a switch: no terminal ever changes character
/// in place.
/// </summary>
public class TerminalLoginServiceTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _clients = [];
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public TerminalLoginServiceTests()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("wiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
	}

	private async Task<(AccountAuthService Auth, ITerminalService Terminal, TerminalLoginService Service)> BuildAsync(
		string? mintOtt = "the-ott")
	{
		var handler = new TerminalLoginApiHandler { MintOtt = mintOtt };
		var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(client);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var (ok, err, _) = await auth.LoginAsync("wiz", "pw");
		if (!ok) throw new InvalidOperationException($"login failed: {err}");

		var terminal = Substitute.For<ITerminalService>();
		var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		return (auth, terminal, new TerminalLoginService(terminal, auth, nav));
	}

	[Test]
	public async Task ConnectAsCharacterAsync_mints_ott_commits_active_character_and_connects_the_terminal()
	{
		var (auth, terminal, service) = await BuildAsync();

		var ok = await service.ConnectAsCharacterAsync(Beta);

		await Assert.That(ok).IsTrue();
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
		await terminal.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "the-ott");
	}

	[Test]
	public async Task ConnectAsCharacterAsync_returns_false_and_does_not_connect_when_the_ott_mint_fails()
	{
		var (auth, terminal, service) = await BuildAsync(mintOtt: null);

		var ok = await service.ConnectAsCharacterAsync(Beta);

		await Assert.That(ok).IsFalse();
		await Assert.That(auth.ActiveCharacter).IsNull();
		await terminal.DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>());
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var c in _clients) c.Dispose();
		await base.DisposeAsync();
	}
}
