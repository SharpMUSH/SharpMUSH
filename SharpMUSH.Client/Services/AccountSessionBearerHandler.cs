using System.Net.Http.Headers;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Attaches the live account-session bearer token to every outgoing request on the <c>"api"</c>
/// HttpClient. No-ops when there is no token (anonymous browsing) or when the caller already set its
/// own <c>Authorization</c> header.
/// </summary>
public sealed class AccountSessionBearerHandler(IAccountAuthState accountAuth) : DelegatingHandler
{
	private readonly IAccountAuthState _accountAuth = accountAuth;

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Headers.Authorization is null)
		{
			// Hydrate before reading the token. On a page refresh the session lives in sessionStorage
			// until InitAsync copies it into memory, so a request issued inside that window would go out
			// anonymous and come back 401. Today every authenticated caller happens to hydrate first —
			// either behind an [Authorize] page (AuthorizeRouteView awaits AccountAuthStateProvider,
			// which awaits InitAsync) or by calling InitAsync itself — but nothing enforces that, and a
			// caller that forgets fails intermittently rather than loudly. Single-flight and JS-interop
			// only: no HTTP, so this cannot re-enter the pipeline, and it is a no-op after the first call.
			await _accountAuth.InitAsync();

			if (_accountAuth.AccountSessionToken is { } token)
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		return await base.SendAsync(request, cancellationToken);
	}
}
