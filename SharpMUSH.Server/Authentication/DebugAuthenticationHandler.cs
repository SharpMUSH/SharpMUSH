using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Development-only authentication handler that automatically authenticates all requests
/// as an admin user, mirroring the client-side DebugAuthStateProvider.
/// </summary>
public class DebugAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "DebugAuth";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var claims = new[]
		{
			new Claim(ClaimTypes.Name, "DebugAdmin"),
			new Claim(ClaimTypes.NameIdentifier, "1"),
			new Claim(ClaimTypes.Role, "Admin")
		};
		var identity = new ClaimsIdentity(claims, SchemeName);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, SchemeName);

		return Task.FromResult(AuthenticateResult.Success(ticket));
	}
}
