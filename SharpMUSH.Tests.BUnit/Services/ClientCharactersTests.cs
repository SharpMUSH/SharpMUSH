using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Signs in as <see cref="Username"/> with the given characters and mints a terminal OTT for each, running
/// <see cref="BeforeToken"/> first when it is set.
/// </summary>
internal sealed class ClientApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	public string Username { get; set; } = "headwiz";

	public Func<Task>? BeforeToken { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri!.AbsolutePath == "/api/auth/mush-token" && BeforeToken is { } before) await before();
		return Respond(request);
	}

	private HttpResponseMessage Respond(HttpRequestMessage request) =>
		request.RequestUri!.AbsolutePath switch
		{
			"/api/auth/account-login" => new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1",
					username = Username,
					characters,
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = "Player",
					permissions = Array.Empty<string>(),
				})
			},
			"/api/auth/mush-token" => new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = JsonContent.Create(new { token = "minted-ott", expiresIn = 60 }) },
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		};
}

/// <summary>
/// <see cref="ClientCharacters"/>: on <c>/client</c> every character of the account connects without the player
/// adding it, the one the tab acts as through Play and the others in the background.
/// </summary>
public class ClientCharactersTests : TrackingBunitContext
{
	private static readonly CharacterSummary Ash = new(5, 1L, "Ash", "", IsActing: true);
	private static readonly CharacterSummary Brin = new(6, 1L, "Brin", "");
	private static readonly CharacterSummary Cole = new(7, 1L, "Cole", "");

	private sealed record Rig(
		ClientCharacters Characters, CharacterSessionRegistry Sessions, List<IPlayTerminalService> Opened,
		AccountAuthService Auth, ClientApiHandler Api);

	private async Task<Rig> BuildAsync(params CharacterSummary[] characters)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		var api = new ClientApiHandler(characters);
		var http = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		// Read the tab's (empty) storage first, as the portal does on boot, so a later read does not sign the tab out.
		await auth.InitAsync();
		if (await auth.LoginAsync("headwiz", "password") is ApiFailure failure) throw new InvalidOperationException(failure.Message);

		var opened = new List<IPlayTerminalService>();
		var sessions = new CharacterSessionRegistry(Session(Terminal(new TerminalIdentity("headwiz", "#5:1"))), () =>
		{
			var terminal = Terminal(null);
			opened.Add(terminal);
			return Session(terminal);
		});
		var service = new ClientCharacters(sessions, auth, Services.GetRequiredService<NavigationManager>(),
			NullLogger<ClientCharacters>.Instance);
		return new Rig(service, sessions, opened, auth, api);
	}

	private static IPlayTerminalService Terminal(TerminalIdentity? identity)
	{
		var terminal = Substitute.For<IPlayTerminalService>();
		terminal.Identity.Returns(identity);
		terminal.OobChannels.Returns(new OobChannelStore());
		return terminal;
	}

	private static CharacterSession Session(IPlayTerminalService terminal) =>
		new(new PlayTerminalServiceHost(() => terminal), Substitute.For<ICommFeed>(), Substitute.For<IChannelWho>());

	[Test]
	public async Task Every_character_but_the_one_in_front_connects_in_the_background()
	{
		var rig = await BuildAsync(Ash, Brin, Cole);

		await rig.Characters.ConnectAllAsync();

		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(3);
		await Assert.That(rig.Opened.Count).IsEqualTo(2);
		await rig.Opened[0].Received(1).ConnectWithOttAsync(Arg.Any<string>(), "minted-ott", new TerminalIdentity("headwiz", "#6:1"));
		await rig.Opened[1].Received(1).ConnectWithOttAsync(Arg.Any<string>(), "minted-ott", new TerminalIdentity("headwiz", "#7:1"));
	}

	[Test]
	public async Task A_second_call_in_the_same_page_load_connects_no_one_again()
	{
		var rig = await BuildAsync(Ash, Brin, Cole);
		await rig.Characters.ConnectAllAsync();
		await rig.Sessions.CloseAsync(rig.Sessions.Sessions[2]);

		await rig.Characters.ConnectAllAsync();

		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(2).Because("a character logged out stays out until picked again");
	}

	[Test]
	public async Task Until_the_tab_knows_who_it_acts_as_nothing_connects()
	{
		var rig = await BuildAsync(Ash with { IsActing = false }, Brin, Cole);

		await rig.Characters.ConnectAllAsync();

		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(1);
	}

	[Test]
	public async Task The_rail_shows_at_most_the_tabs_limit()
	{
		var many = Enumerable.Range(10, CharacterSessionRegistry.Limit + 2).Select(n => new CharacterSummary(n, 1L, $"C{n}", "")).ToArray();
		var rig = await BuildAsync([Ash, .. many]);

		await Assert.That(rig.Characters.Shown.Count).IsEqualTo(CharacterSessionRegistry.Limit);
		await Assert.That(rig.Characters.Shown[0]).IsEqualTo(Ash);
	}

	[Test]
	public async Task Another_account_signing_in_on_the_same_page_has_its_characters_connected()
	{
		var rig = await BuildAsync(Ash, Brin, Cole);
		await rig.Characters.ConnectAllAsync();
		foreach (var session in rig.Sessions.Sessions.Skip(1).ToList()) await rig.Sessions.CloseAsync(session);

		rig.Api.Username = "otherwiz";
		if (await rig.Auth.LoginAsync("otherwiz", "password") is ApiFailure failure) throw new InvalidOperationException(failure.Message);
		await rig.Characters.ConnectAllAsync();

		await Assert.That(rig.Sessions.Sessions.Count).IsEqualTo(3);
	}

	[Test]
	public async Task A_session_closed_while_its_token_is_on_the_way_does_not_connect()
	{
		var rig = await BuildAsync(Ash, Brin);
		// Signing out closes the background session before the server answers.
		rig.Api.BeforeToken = () => rig.Sessions.CloseAsync(rig.Sessions.Sessions[^1]);

		await rig.Characters.ConnectAllAsync();

		await Assert.That(rig.Opened.Count).IsEqualTo(1);
		await rig.Opened[0].DidNotReceive().ConnectWithOttAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TerminalIdentity?>());
	}
}
