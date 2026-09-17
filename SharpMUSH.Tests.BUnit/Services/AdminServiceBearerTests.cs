using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Records what actually reached the wire, then answers with an empty list.</summary>
internal sealed class RosterHandler : HttpMessageHandler
{
	public AuthenticationHeaderValue? LastAuthorization { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		LastAuthorization = request.Headers.Authorization;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(new
			{
				guestLoginsEnabled = true,
				maxGuests = -1,
				nextFreeName = "Guest1",
				guests = Array.Empty<object>()
			})
		});
	}
}

/// <summary>
/// The admin services used to build their own <c>Authorization</c> header from
/// <c>AccountAuthService.AccountSessionToken</c>. That is not merely redundant with
/// <see cref="AccountSessionBearerHandler"/> — it defeats it. The handler only acts when the caller
/// left <c>Authorization</c> unset, and hydrating the session from <c>sessionStorage</c> is the first
/// thing it does. A caller that sets the header eagerly reads the token before hydration, sends a
/// bare <c>Bearer</c> with no value on a page refresh, and the handler then steps aside because a
/// header is present — so the admin pages 401 on refresh where every other page works.
/// </summary>
public class AdminServiceBearerTests : TrackingBunitContext
{
	private (IHttpClientFactory Factory, AccountAuthService Auth, RosterHandler Wire) Session(string? storedToken)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(storedToken);

		var wire = new RosterHandler();
		var factory = Substitute.For<IHttpClientFactory>();

		// The auth service reads its own client; give it one that does not run the bearer handler, so
		// the assertions below are about the admin call and nothing else.
		var authHttp = Track(new HttpClient(new RosterHandler()) { BaseAddress = new Uri("https://localhost/") });
		var auth = new AccountAuthService(
			AuthOnlyFactory(authHttp),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance,
			[]);

		var apiHttp = Track(new HttpClient(new AccountSessionBearerHandler(auth) { InnerHandler = wire })
		{
			BaseAddress = new Uri("https://localhost/")
		});
		factory.CreateClient("api").Returns(apiHttp);

		return (factory, auth, wire);
	}

	private static IHttpClientFactory AuthOnlyFactory(HttpClient http)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return factory;
	}

	/// <summary>
	/// Nobody has awaited <c>InitAsync</c> — this is the guest roster loading during a page refresh.
	/// </summary>
	[Test]
	public async Task GuestRosterSendsTheStoredTokenWithoutPriorInitAsync()
	{
		var (factory, _, wire) = Session("stored-session-token");

		await new AdminGuestsService(factory).ListAsync();

		await Assert.That(wire.LastAuthorization?.Scheme).IsEqualTo("Bearer");
		await Assert.That(wire.LastAuthorization?.Parameter).IsEqualTo("stored-session-token");
	}

	[Test]
	public async Task AccountListSendsTheStoredTokenWithoutPriorInitAsync()
	{
		var (factory, _, wire) = Session("stored-session-token");

		await new AdminAccountsService(factory).ListAsync();

		await Assert.That(wire.LastAuthorization?.Parameter).IsEqualTo("stored-session-token");
	}

	/// <summary>
	/// An anonymous visitor must not have a credential invented for them, and must not send an empty
	/// <c>Bearer</c> either — a header with no value is a malformed credential, not the absence of one.
	/// </summary>
	[Test]
	public async Task AnAnonymousCallerSendsNoAuthorizationHeaderAtAll()
	{
		var (factory, _, wire) = Session(storedToken: null);

		await new AdminGuestsService(factory).ListAsync();

		await Assert.That(wire.LastAuthorization).IsNull();
	}
}
