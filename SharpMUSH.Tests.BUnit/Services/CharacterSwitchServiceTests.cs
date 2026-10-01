using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Net.Http.Json;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Answers the switch endpoint with a token bound to the requested character.</summary>
file sealed class SwitchApiHandler(bool succeed = true) : HttpMessageHandler
{
	public int Calls { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri?.AbsolutePath == "/api/account/session")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = JsonContent.Create(new { username = "current", mustChangePassword = false, role = "Player", permissions = Array.Empty<string>() }) });
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
/// the tab adopts it, then the hub reconnects so it re-authenticates with that token. It never touches
/// the terminals (a terminal's character is fixed at connect).
/// </summary>
public class CharacterSwitchServiceTests : TrackingBunitContext
{
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	private (AccountAuthService Auth, IConnectionStateService Connection, CharacterSwitchService Service) Build(bool succeed = true)
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
		return (auth, connection, new CharacterSwitchService(auth, connection, new TerminalResumeStore(JSInterop.JSRuntime)));
	}

	[Test]
	public async Task SwitchAsync_adopts_the_token_the_server_bound_to_the_target()
	{
		var (auth, _, service) = Build();

		var switched = await service.SwitchAsync(Beta);

		await Assert.That(switched).IsTrue();
		await Assert.That(auth.AccountSessionToken).IsEqualTo("bound-to-beta");
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
	}

	[Test]
	public async Task SwitchAsync_reconnects_the_game_hub_so_it_reauthenticates_with_the_new_token()
	{
		var (_, connection, service) = Build();

		await service.SwitchAsync(Beta);

		await connection.Received(1).ReconnectAsync();
	}

	[Test]
	public async Task SwitchAsync_refused_by_the_server_keeps_the_current_identity()
	{
		var (auth, connection, service) = Build(succeed: false);

		var switched = await service.SwitchAsync(Beta);

		// A refused switch must not leave the tab claiming a character its token does not name.
		await Assert.That(switched).IsFalse();
		await Assert.That(auth.AccountSessionToken).IsEqualTo("inherited-token");
		await Assert.That(auth.ActiveCharacter).IsNull();
		await connection.DidNotReceive().ReconnectAsync();
		await Assert.That(JSInterop.Invocations["SharpMUSH.Resume.removeAll"]).IsEmpty()
			.Because("a refused switch leaves the tab as it was, resume points included");
	}

	/// <summary>
	/// A terminal still open as the previous character must not leave a point a later reload could
	/// resume: the switch forgets every stored point and revokes the open terminals' slots.
	/// </summary>
	[Test]
	public async Task SwitchAsync_forgets_every_stored_resume_point()
	{
		var (_, _, service) = Build();

		await service.SwitchAsync(Beta);

		var cleared = JSInterop.VerifyInvoke("SharpMUSH.Resume.removeAll");
		await Assert.That(cleared.Arguments[0]).IsEqualTo(TerminalResumeStore.KeyPrefix);
	}
}
