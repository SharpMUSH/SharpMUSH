using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Development-only authentication handler that automatically authenticates all requests
/// as the bootstrap admin account (the account linked to player #1), mirroring the
/// client-side DebugAuthStateProvider.
///
/// Emits the same claim set that <see cref="JwtService"/> would produce so that
/// <see cref="GameHub"/>, <see cref="Controllers.ApiControllerBase"/>, and all other
/// server-side consumers see a fully-populated principal with no dev-mode special cases.
///
/// Falls back to static placeholder claims if the DB is not yet initialised (e.g. during
/// a very early request before <see cref="Services.BootstrapService"/> has run).
/// </summary>
public class DebugAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IAccountService accountService)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "DebugAuth";

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var claims = new List<Claim> { new(ClaimTypes.Role, "Admin") };
		// The bootstrap account owns player #1 (God), the top of the hierarchy. Role
		// checks are exact string matches, so emit a claim for every PortalRole — this
		// way God- and Wizard-gated endpoints both authorize under debug auth.
		claims.AddRange(Enum.GetNames<Library.Authorization.PortalRole>()
			.Select(name => new Claim(ClaimTypes.Role, name)));

		try
		{
			// Look up the account that BootstrapService linked to #1.
			var account = await accountService.GetAccountForCharacterAsync(new DBRef(1));

			if (account is not null)
			{
				claims.Add(new(ClaimTypes.NameIdentifier, account.Id!));
				claims.Add(new(ClaimTypes.Name, account.Username));

				// GetCharactersAsync is cheaper than a separate mediator query and
				// already returns the full SharpPlayer with key, creation time, and name.
				var characters = await accountService.GetCharactersAsync(account.Id!);
				var player = characters.FirstOrDefault(c => c.Object.Key == 1);
				if (player is not null)
				{
					claims.Add(new("character_key", player.Object.Key.ToString()));
					claims.Add(new("character_creation_time", player.Object.CreationTime.ToString()));
					claims.Add(new("character_name", player.Object.Name));
					// GameHub uses this claim for SignalR character-group routing.
					claims.Add(new(GameHub.CharacterDbrefClaim, $"#{player.Object.Key}"));
				}
				else
				{
					// Account exists but character list lookup returned nothing (shouldn't happen
					// after a successful bootstrap, but guard defensively).
					Logger.LogWarning(
						"[DebugAuth] Account {AccountId} has no linked character with key 1; omitting character claims",
						account.Id);
					claims.Add(new("character_key", "1"));
					claims.Add(new(GameHub.CharacterDbrefClaim, "#1"));
				}
			}
			else
			{
				// Bootstrap hasn't run yet (e.g. first request races DB initialisation).
				// Emit static fallback claims so the server doesn't crash on early requests.
				Logger.LogWarning(
					"[DebugAuth] No account linked to #1 yet; using static fallback claims. " +
					"This is expected only on the very first startup before BootstrapService completes.");
				EmitStaticFallback(claims);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "[DebugAuth] Failed to resolve bootstrap account; using static fallback claims");
			EmitStaticFallback(claims);
		}

		var identity = new ClaimsIdentity(claims, SchemeName);
		var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
		return AuthenticateResult.Success(ticket);
	}

	/// <summary>
	/// Emits minimal hard-coded claims when the DB is not yet available.
	/// All callers that care about a real account ID will receive a sentinel value and
	/// must handle the "account not found" path gracefully.
	/// </summary>
	private static void EmitStaticFallback(List<Claim> claims)
	{
		claims.Add(new(ClaimTypes.Name, "DebugAdmin"));
		claims.Add(new(ClaimTypes.NameIdentifier, "debug-bootstrap-pending"));
		claims.Add(new("character_key", "1"));
		claims.Add(new(GameHub.CharacterDbrefClaim, "#1"));
	}
}
