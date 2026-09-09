using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>Refreshes tab display claims from the current account and effective permissions.</summary>
[ApiController]
[Route("api/account/session")]
public class AccountSessionController(IAccountSessionStore sessions, IAccountService accounts,
	AccountClaimsService claims, IAdministrativeCapabilityService capabilities) : ControllerBase
{
	public record SessionStateResponse(string Username, bool MustChangePassword, string Role, IReadOnlyList<string> Permissions);

	[HttpGet]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public async Task<IActionResult> GetSession(CancellationToken ct)
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
		var session = await sessions.ValidateAsync(header["Bearer ".Length..].Trim(), ct);
		if (session is null) return Unauthorized();
		var account = await accounts.GetByIdAsync(session.Value.AccountId, ct);
		if (account is null || !account.IsActive) return Unauthorized();
		var role = await claims.ComputeAccountRoleAsync(session.Value.AccountId, ct);
		// Reuse the server authorization resolver, including explicit child denials.
		var scopes = await capabilities.GetGrantedScopesAsync(new(session.Value.AccountId), ct);
		return Ok(new SessionStateResponse(account.Username, account.MustChangePassword, role.ToString(), scopes.ToArray()));
	}
}
