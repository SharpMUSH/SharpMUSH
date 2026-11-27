using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Answers the switch endpoint with a token bound to the requested character, and mints terminal OTTs. The first
/// switch's token is <c>bound-to-beta</c>, each later one's <c>bound-to-&lt;call&gt;</c>; switches after
/// <paramref name="succeedFor"/> of them are refused.
/// </summary>
file sealed class SwitchApiHandler(bool succeed = true, int succeedFor = int.MaxValue) : HttpMessageHandler
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
		if (!succeed || Calls > succeedFor)
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new { ott = "ott-1", expiresIn = 60, accountSessionToken = Calls == 1 ? "bound-to-beta" : $"bound-to-{Calls}" })
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
	private static readonly CharacterSummary Alpha = new(1, 1L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");
	private static readonly CharacterSummary Gamma = new(3, 3L, "Gamma", "");
	private static readonly CharacterSummary Delta = new(4, 4L, "Delta", "");

	private sealed record Rig(
		AccountAuthService Auth,
		IConnectionStateService Connection,
		ITerminalService CommandFirst, ITerminalService CommandSecond,
		IPlayTerminalService PlayFirst, IPlayTerminalService PlaySecond,
		CharacterSwitchService Service, CharacterSessionRegistry Sessions, IPlayTerminalService Opened);

	private Rig Build(bool succeed = true, bool connected = true, bool keeps = false, int succeedFor = int.MaxValue)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("inherited-token");

		var api = new SwitchApiHandler(succeed, succeedFor);
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
		var commands = new Queue<ITerminalService>([commandFirst, commandSecond]);
		// A second switch rebuilds the command terminal again.
		var commandHost = new TerminalServiceHost(() => commands.Count > 0 ? commands.Dequeue() : Substitute.For<ITerminalService>());

		var playFirst = Substitute.For<IPlayTerminalService>();
		var playSecond = Substitute.For<IPlayTerminalService>();
		playFirst.IsConnected.Returns(connected);
		var playHost = new PlayTerminalServiceHost(new Queue<IPlayTerminalService>([playFirst, playSecond]).Dequeue);

		// Each further session's play connection, for the client, which keeps every character connected.
		var opened = Substitute.For<IPlayTerminalService>();
		var sessions = new CharacterSessionRegistry(
			new CharacterSession(playHost, Substitute.For<ICommFeed>(), Substitute.For<IChannelWho>()),
			() => new CharacterSession(new PlayTerminalServiceHost(() => opened), Substitute.For<ICommFeed>(), Substitute.For<IChannelWho>()))
		{ KeepsSessions = keeps };
		var service = new CharacterSwitchService(auth, commandHost, sessions, connection,
			new TerminalResumeStore(JSInterop.JSRuntime), Services.GetRequiredService<NavigationManager>(),
			NullLogger<CharacterSwitchService>.Instance);
		return new Rig(auth, connection, commandFirst, commandSecond, playFirst, playSecond, service, sessions, opened);
	}

	[Test]
	public async Task SwitchAsync_adopts_the_token_the_server_bound_to_the_target()
	{
		var rig = Build();

		var switched = await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		await Assert.That(switched).IsTrue();
		await Assert.That(rig.Auth.AccountSessionToken).IsEqualTo("bound-to-beta");
		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
	}

	[Test]
	public async Task SwitchAsync_reconnects_the_game_hub_so_it_reauthenticates_with_the_new_token()
	{
		var rig = Build();

		await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

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
		await rig.Service.Settled;

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
		await rig.Service.Settled;

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
		await rig.Service.Settled;

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
		await rig.Service.Settled;

		var cleared = JSInterop.VerifyInvoke("SharpMUSH.Resume.removeAll");
		await Assert.That(cleared.Arguments[0]).IsEqualTo(TerminalResumeStore.KeyPrefix);
	}

	/// <summary>
	/// On the client a switch opens a session for the character, connects it with the switch's token and brings it
	/// to the front; the character that was in front stays connected, and every resume point stays for a reload.
	/// </summary>
	[Test]
	public async Task SwitchAsync_on_the_client_opens_a_session_and_keeps_the_other_connected()
	{
		var rig = Build(keeps: true);
		var first = rig.Sessions.Focused;

		await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		var beta = new TerminalIdentity("current", "#2:2");
		await rig.Opened.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "ott-1", beta);
		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(2);
		await Assert.That(ReferenceEquals(rig.Sessions.Focused, first)).IsFalse();
		await rig.PlayFirst.DidNotReceive().SendAsync("QUIT");
		await rig.PlayFirst.DidNotReceive().DisposeAsync();
		await Assert.That(JSInterop.Invocations["SharpMUSH.Resume.removeAll"]).IsEmpty();
		// The command terminal is the tab's portal connection, and acts as the character in front.
		await rig.CommandSecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "minted-ott", beta);
	}

	/// <summary>A character the tab already plays comes to the front as it is: no new login, no QUIT.</summary>
	[Test]
	public async Task SwitchAsync_to_a_character_already_open_brings_its_session_forward()
	{
		var rig = Build(keeps: true);
		var first = rig.Sessions.Focused;
		await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;
		var betaSession = rig.Sessions.Focused;
		rig.Opened.Identity.Returns(new TerminalIdentity("current", "#2:2"));
		rig.Sessions.Focus(first);
		rig.Opened.ClearReceivedCalls();

		await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		await Assert.That(ReferenceEquals(rig.Sessions.Focused, betaSession)).IsTrue();
		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(2);
		await rig.Opened.DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>());
		await rig.Opened.DidNotReceive().SendAsync("QUIT");
	}

	/// <summary>A full client tab plays no new character: the one in front is not logged out to make room.</summary>
	[Test]
	public async Task SwitchAsync_on_a_full_client_tab_to_a_character_not_open_does_nothing()
	{
		var rig = Build(keeps: true);
		while (rig.Sessions.CanOpen) rig.Sessions.Open();

		var switched = await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		await Assert.That(switched).IsFalse();
		await rig.PlayFirst.DidNotReceive().SendAsync("QUIT");
		await rig.Connection.DidNotReceive().ReconnectAsync();
	}

	/// <summary>A character whose connection fails does not keep a place in the tab.</summary>
	[Test]
	public async Task SwitchAsync_on_the_client_closes_the_session_whose_connection_failed()
	{
		var rig = Build(keeps: true);
		rig.Opened.ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>())
			.Returns(Task.FromException(new InvalidOperationException("gateway down")));
		var first = rig.Sessions.Focused;
		rig.Auth.SetActiveCharacter(Alpha);

		var switched = await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		await Assert.That(switched).IsFalse();
		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(1);
		await Assert.That(ReferenceEquals(rig.Sessions.Focused, first)).IsTrue();
		// The website acts as the character still in front, as its play terminal does, on the token bound back to it.
		await Assert.That(rig.Auth.ActiveCharacter?.DbrefNumber).IsEqualTo(Alpha.DbrefNumber);
		await Assert.That(rig.Auth.AccountSessionToken).IsEqualTo("bound-to-2");
		await rig.PlayFirst.DidNotReceive().DisconnectAsync();
	}

	/// <summary>
	/// When the server will not switch back either, the tab acts as the character that failed to connect, so the
	/// terminal in front stops playing the one before: nothing goes on sending as a character the tab no longer is.
	/// </summary>
	[Test]
	public async Task SwitchAsync_on_the_client_hangs_up_the_front_session_when_it_cannot_switch_back()
	{
		var rig = Build(keeps: true, succeedFor: 1);
		rig.Opened.ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>())
			.Returns(Task.FromException(new InvalidOperationException("gateway down")));
		rig.Auth.SetActiveCharacter(Alpha);

		var switched = await rig.Service.SwitchAsync(Beta);
		await rig.Service.Settled;

		await Assert.That(switched).IsFalse();
		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(1);
		await rig.PlayFirst.Received(1).SendAsync("QUIT");
		await rig.PlayFirst.Received(1).DisconnectAsync();
		await rig.CommandFirst.Received(1).SendAsync("QUIT");
		await rig.CommandFirst.Received(1).DisconnectAsync();
	}

	/// <summary>
	/// A switch is done once its character is in front: the command terminal and the hub follow behind it, so the next
	/// switch need not wait for them. Switches that land while they follow one are followed only to the last.
	/// </summary>
	[Test]
	public async Task SwitchAsync_returns_before_the_command_terminal_follows_and_it_follows_only_the_last()
	{
		var rig = Build(keeps: true);
		var quitting = new TaskCompletionSource();
		rig.CommandFirst.SendAsync("QUIT").Returns(quitting.Task);
		rig.CommandSecond.IsConnected.Returns(true);

		await rig.Service.SwitchAsync(Beta).WaitAsync(TimeSpan.FromSeconds(5));
		// The command terminal is on its way to Beta, waiting on its QUIT.
		for (var spins = 0; spins < 100_000 && !rig.CommandFirst.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ITerminalService.SendAsync)); spins++)
			await Task.Yield();
		await Assert.That(await rig.Service.SwitchAsync(Gamma).WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
		await Assert.That(await rig.Service.SwitchAsync(Delta).WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
		await Assert.That(rig.Service.Settled.IsCompleted).IsFalse();

		quitting.SetResult();
		await rig.Service.Settled.WaitAsync(TimeSpan.FromSeconds(5));

		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(4);
		await rig.CommandSecond.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "minted-ott", new TerminalIdentity("current", "#2:2"));
		// Gamma came and went while the command terminal was still on its way to Beta.
		await rig.CommandSecond.Received(1).SendAsync("QUIT");
		await rig.Connection.Received(2).ReconnectAsync();
	}
}
