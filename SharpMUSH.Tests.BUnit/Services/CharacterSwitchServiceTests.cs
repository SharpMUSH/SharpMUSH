using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Net.Http.Json;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Answers the switch endpoint with a token bound to the requested character, and mints terminal OTTs.</summary>
file sealed class SwitchApiHandler(bool succeed = true) : HttpMessageHandler
{
	public int Calls { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri?.AbsolutePath == "/api/account/session")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = JsonContent.Create(new { username = "current", mustChangePassword = false, role = "Player", permissions = Array.Empty<string>() }) });
		if (request.RequestUri?.AbsolutePath == "/api/auth/mush-token")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = JsonContent.Create(new { token = "minted-ott", expiresIn = 60 }) });
		Calls++;
		if (!succeed)
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new { ott = "ott-1", expiresIn = 60, accountSessionToken = "bound-to-beta" })
		});
	}
}

/// <summary>
/// Coverage for <see cref="CharacterSwitchService"/>, the account-panel switch of the portal's acting
/// character. The switch is a server-side rebind: the endpoint mints a token bound to the target and
/// the tab adopts it, then the hub reconnects so it re-authenticates with that token, and every terminal
/// that was connected quits the previous character and connects as the new one.
/// </summary>
public class CharacterSwitchServiceTests : TrackingBunitContext
{
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	private sealed record Rig(
		AccountAuthService Auth,
		IConnectionStateService Connection,
		ITerminalService CommandFirst, ITerminalService CommandSecond,
		IPlayTerminalService PlayFirst, IPlayTerminalService PlaySecond,
		CharacterSwitchService Service);

	private Rig Build(bool succeed = true, bool connected = true)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("inherited-token");

		var api = new SwitchApiHandler(succeed);
		// Not disposed: the service keeps calling through this client after Build returns.
		var http = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var auth = new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			[]);
		var connection = Substitute.For<IConnectionStateService>();

		var commandFirst = Substitute.For<ITerminalService>();
		var commandSecond = Substitute.For<ITerminalService>();
		commandFirst.IsConnected.Returns(connected);
		var commandHost = new TerminalServiceHost(new Queue<ITerminalService>([commandFirst, commandSecond]).Dequeue);

		var playFirst = Substitute.For<IPlayTerminalService>();
		var playSecond = Substitute.For<IPlayTerminalService>();
		playFirst.IsConnected.Returns(connected);
		var playHost = new PlayTerminalServiceHost(new Queue<IPlayTerminalService>([playFirst, playSecond]).Dequeue);

		var service = new CharacterSwitchService(auth, commandHost, playHost, connection,
			new TerminalResumeStore(JSInterop.JSRuntime), Services.GetRequiredService<NavigationManager>());
		return new Rig(auth, connection, commandFirst, commandSecond, playFirst, playSecond, service);
	}

	[Test]
	public async Task SwitchAsync_adopts_the_token_the_server_bound_to_the_target()
	{
		var rig = Build();

		var switched = await rig.Service.SwitchAsync(Beta);

		await Assert.That(switched).IsTrue();
		await Assert.That(rig.Auth.AccountSessionToken).IsEqualTo("bound-to-beta");
		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
	}

	[Test]
	public async Task SwitchAsync_reconnects_the_game_hub_so_it_reauthenticates_with_the_new_token()
	{
		var rig = Build();

		await rig.Service.SwitchAsync(Beta);

		await rig.Connection.Received(1).ReconnectAsync();
	}

	/// <summary>
	/// The terminal and the Play page follow the switch: each connected terminal ends the previous
	/// character's session with QUIT (a bare close would leave it online through the grace window), is
	/// rebuilt, and connects as the new character.
	/// </summary>
	[Test]
	public async Task SwitchAsync_moves_connected_terminals_to_the_new_character()
	{
		var rig = Build();

		await rig.Service.SwitchAsync(Beta);

		await rig.PlayFirst.Received(1).SendAsync("QUIT");
		await rig.PlayFirst.Received(1).DisposeAsync();
		var beta = new TerminalIdentity("current", "#2:2");
		await rig.PlaySecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "ott-1", beta);
		await rig.CommandFirst.Received(1).SendAsync("QUIT");
		await rig.CommandFirst.Received(1).DisposeAsync();
		await rig.CommandSecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "minted-ott", beta);
		await Assert.That(rig.PlaySecond.ConnectedPlayerName).IsEqualTo("Beta");
	}

	/// <summary>A terminal that was not connected is rebuilt but left for its next connect, which uses the active character.</summary>
	[Test]
	public async Task SwitchAsync_leaves_idle_terminals_disconnected()
	{
		var rig = Build(connected: false);

		await rig.Service.SwitchAsync(Beta);

		await rig.PlayFirst.DidNotReceive().SendAsync("QUIT");
		await rig.PlayFirst.Received(1).DisposeAsync();
		await rig.PlaySecond.DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>());
		await rig.CommandSecond.DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>());
	}

	[Test]
	public async Task SwitchAsync_refused_by_the_server_keeps_the_current_identity()
	{
		var rig = Build(succeed: false);

		var switched = await rig.Service.SwitchAsync(Beta);

		// A refused switch must not leave the tab claiming a character its token does not name.
		await Assert.That(switched).IsFalse();
		await Assert.That(rig.Auth.AccountSessionToken).IsEqualTo("inherited-token");
		await Assert.That(rig.Auth.ActiveCharacter).IsNull();
		await rig.Connection.DidNotReceive().ReconnectAsync();
		await rig.PlayFirst.DidNotReceive().SendAsync("QUIT");
		await rig.PlayFirst.DidNotReceive().DisposeAsync();
		await Assert.That(JSInterop.Invocations["SharpMUSH.Resume.removeAll"]).IsEmpty()
			.Because("a refused switch leaves the tab as it was, resume points included");
	}

	/// <summary>
	/// No terminal may leave a point a later reload could resume as the previous character: the switch
	/// forgets every stored point.
	/// </summary>
	[Test]
	public async Task SwitchAsync_forgets_every_stored_resume_point()
	{
		var rig = Build();

		await rig.Service.SwitchAsync(Beta);

		var cleared = JSInterop.VerifyInvoke("SharpMUSH.Resume.removeAll");
		await Assert.That(cleared.Arguments[0]).IsEqualTo(TerminalResumeStore.KeyPrefix);
	}
}
