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

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Headers.Authorization is null && _accountAuth.AccountSessionToken is { } token)
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return base.SendAsync(request, cancellationToken);
	}
}
