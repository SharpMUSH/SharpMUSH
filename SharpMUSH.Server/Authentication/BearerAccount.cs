using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>The account a request's account-session bearer names.</summary>
public readonly record struct SignedInAccount(string Id);

/// <summary>The account a request is signed in as, or the response that refuses it.</summary>
public union BearerAccount(SignedInAccount, ActionResult);

/// <summary>
/// Resolves the account behind a controller's request. The AccountSession handler has usually validated the
/// session and the account already; under another scheme (DebugAuth in Development), or none, the bearer is
/// validated here.
/// </summary>
public sealed class BearerAccountResolver(IAccountSessionStore sessions, IAccountService accounts)
{
	/// <summary>
	/// The signed-in account. Unless <paramref name="allowMustChangePassword"/>, an account flagged to change its
	/// password is refused: server-side, not advisory, a flagged session may only change its password or log out.
	/// </summary>
	public async Task<BearerAccount> ResolveAsync(ControllerBase controller, bool allowMustChangePassword = false)
	{
		if (AccountSessionAuthenticationHandler.TryGetAccount(controller.User, out var authenticated, out var mustChange))
		{
			return !allowMustChangePassword && mustChange
				? PasswordChangeRequired(controller)
				: new SignedInAccount(authenticated);
		}

		var header = controller.Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				|| await sessions.ValidateAsync(header["Bearer ".Length..].Trim()) is not { } session)
		{
			return controller.Unauthorized("Invalid or expired account session.");
		}

		return await accounts.GetByIdAsync(session.AccountId) switch
		{
			null or { IsActive: false } => controller.Unauthorized("Account not found or not active."),
			{ MustChangePassword: true } when !allowMustChangePassword => PasswordChangeRequired(controller),
			_ => new SignedInAccount(session.AccountId)
		};
	}

	private static ObjectResult PasswordChangeRequired(ControllerBase controller)
		=> controller.StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action.");
}
