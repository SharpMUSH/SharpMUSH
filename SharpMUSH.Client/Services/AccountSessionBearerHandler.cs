using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Attaches the live account-session bearer token to every outgoing request on the <c>"api"</c>
/// HttpClient. No-ops when there is no token (anonymous browsing) or when the caller already set its
/// own <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// A request the server refuses with 401 on the bearer this attached is sent once more with a renewed
/// one, when the browser's remembered login can renew it (<see cref="IAccountAuthState.RenewSessionAsync"/>):
/// a tab left idle past its session's lifetime carries on instead of failing every call.
/// <para>Every request carries the browser's cookies, even to an API on another origin (the standalone
/// client dev server): the remembered login is a cookie, and a fetch left at its same-origin default
/// would neither keep it from a sign-in nor send it back to <c>account-resume</c>.</para>
/// </remarks>
public sealed class AccountSessionBearerHandler(IAccountAuthState accountAuth) : DelegatingHandler
{
	/// <summary>Set on a request that must go out as it is: no hydration wait, no bearer, no renewal.</summary>
	public static readonly HttpRequestOptionsKey<bool> Anonymous = new("sharpmush.anonymous");

	private readonly IAccountAuthState _accountAuth = accountAuth;

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

		if (request.Options.TryGetValue(Anonymous, out var anonymous) && anonymous
			|| request.Headers.Authorization is not null)
			return await base.SendAsync(request, cancellationToken);

		// Hydrate before reading the token. On a page refresh the session lives in sessionStorage
		// until InitAsync copies it into memory, so a request issued inside that window would go out
		// anonymous and come back 401. Today every authenticated caller happens to hydrate first —
		// either behind an [Authorize] page (AuthorizeRouteView awaits AccountAuthStateProvider,
		// which awaits InitAsync) or by calling InitAsync itself — but nothing enforces that, and a
		// caller that forgets fails intermittently rather than loudly. Single-flight session
		// hydration refresh sends an explicit bearer, so it bypasses this block and cannot re-enter.
		await _accountAuth.InitAsync();

		if (_accountAuth.AccountSessionToken is not { } token)
			return await base.SendAsync(request, cancellationToken);

		// Read before sending: the body has to be sent again if the bearer is renewed.
		var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await base.SendAsync(request, cancellationToken);
		if (response.StatusCode != HttpStatusCode.Unauthorized
			|| await _accountAuth.RenewSessionAsync(token) is not { } renewed)
			return response;

		response.Dispose();
		var retry = Copy(request, body);
		retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
		return await base.SendAsync(retry, cancellationToken);
	}

	/// <summary>A request that has been sent cannot be sent again; this is a fresh one like it.</summary>
	private static HttpRequestMessage Copy(HttpRequestMessage request, byte[]? body)
	{
		var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
		foreach (var header in request.Headers)
			copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
		foreach (var option in request.Options)
			((IDictionary<string, object?>)copy.Options)[option.Key] = option.Value;

		if (body is not null)
		{
			copy.Content = new ByteArrayContent(body);
			foreach (var header in request.Content!.Headers)
				copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}
		return copy;
	}
}
