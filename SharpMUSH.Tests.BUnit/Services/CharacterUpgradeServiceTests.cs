using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Fakes account-login (to seed a real logged-in service) and mush-token (the per-terminal OTT).</summary>
file sealed class UpgradeApiHandler(string? mintOtt) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1", username = "wiz", characters = Array.Empty<object>(),
					accountSessionToken = "session-token-1", mustChangePassword = false, role = "Wizard",
					permissions = new[] { "*" },
				})
			});

		if (request.Method == HttpMethod.Post && path == "api/auth/mush-token")
			return Task.FromResult(mintOtt is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token = mintOtt, expiresIn = 60 }) });

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Coverage for <see cref="CharacterUpgradeService"/> — the "start playing as this character
/// everywhere" flow used on character creation (guest / first character). It commits the active
/// character and cleanly recreates + reconnects BOTH terminals (command and /play) plus the game hub.
/// </summary>
public class CharacterUpgradeServiceTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _clients = [];
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	private sealed record Rig(
		AccountAuthService Auth,
		ITerminalService CommandFirst, ITerminalService CommandSecond,
		IPlayTerminalService PlayFirst, IPlayTerminalService PlaySecond,
		IConnectionStateService Connection,
		CharacterUpgradeService Service);

	private async Task<Rig> BuildAsync(string? mintOtt = "the-ott")
	{
		var client = new HttpClient(new UpgradeApiHandler(mintOtt)) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(client);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("wiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);

		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		var (ok, err, _) = await auth.LoginAsync("wiz", "pw");
		if (!ok) throw new InvalidOperationException($"login failed: {err}");

		var commandFirst = Substitute.For<ITerminalService>();
		var commandSecond = Substitute.For<ITerminalService>();
		var commandHost = new TerminalServiceHost(new Queue<ITerminalService>([commandFirst, commandSecond]).Dequeue);

		var playFirst = Substitute.For<IPlayTerminalService>();
		var playSecond = Substitute.For<IPlayTerminalService>();
		var playHost = new PlayTerminalServiceHost(new Queue<IPlayTerminalService>([playFirst, playSecond]).Dequeue);

		var connection = Substitute.For<IConnectionStateService>();
		var nav = Services.GetRequiredService<NavigationManager>();
		var service = new CharacterUpgradeService(auth, commandHost, playHost, connection, nav);

		return new Rig(auth, commandFirst, commandSecond, playFirst, playSecond, connection, service);
	}

	[Test]
	public async Task PlayAsAsync_commits_active_and_reconnects_both_terminals_and_the_hub()
	{
		var rig = await BuildAsync();

		var ok = await rig.Service.PlayAsAsync(Beta);

		await Assert.That(ok).IsTrue();
		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);

		// Both terminals recreated (old inner disposed) then reconnected on the fresh inner.
		await rig.CommandFirst.Received(1).DisposeAsync();
		await rig.CommandSecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "the-ott");
		await rig.PlayFirst.Received(1).DisposeAsync();
		await rig.PlaySecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "the-ott");

		await rig.Connection.Received(1).ReconnectAsync();
	}

	[Test]
	public async Task PlayAsAsync_touches_nothing_when_an_ott_cannot_be_minted()
	{
		var rig = await BuildAsync(mintOtt: null);

		var ok = await rig.Service.PlayAsAsync(Beta);

		await Assert.That(ok).IsFalse();
		await Assert.That(rig.Auth.ActiveCharacter).IsNull();
		await rig.CommandFirst.DidNotReceive().DisposeAsync();
		await rig.PlayFirst.DidNotReceive().DisposeAsync();
		await rig.Connection.DidNotReceive().ReconnectAsync();
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var c in _clients) c.Dispose();
		await base.DisposeAsync();
	}
}
