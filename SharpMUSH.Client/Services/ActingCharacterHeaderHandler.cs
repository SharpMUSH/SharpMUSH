namespace SharpMUSH.Client.Services;

/// <summary>
/// Advertises the tab's active character on every <c>"api"</c> request via the
/// <c>X-Acting-Character</c> header, so the server routes per-character actions to the switched-to
/// character (validated against the account) rather than the primary. No header when no character is active.
/// </summary>
public sealed class ActingCharacterHeaderHandler(IAccountAuthState accountAuth) : DelegatingHandler
{
	private readonly IAccountAuthState _accountAuth = accountAuth;

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (_accountAuth.ActiveCharacter is { } character && !request.Headers.Contains("X-Acting-Character"))
			request.Headers.Add("X-Acting-Character", $"#{character.DbrefNumber}");

		return base.SendAsync(request, cancellationToken);
	}
}
