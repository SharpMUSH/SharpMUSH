using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication;

/// <summary>The account a request's account-session bearer names.</summary>
public readonly record struct SignedInAccount(string Id);

/// <summary>The account a request is signed in as, or the response that refuses it.</summary>
public union BearerAccount(SignedInAccount, ActionResult);

/// <summary>The account a request's account-session bearer names, and the character the session acts as.</summary>
public sealed record SignedInCharacter(string AccountId, SharpPlayer Character);

/// <summary>The account and character a request is signed in as, or the response that refuses it.</summary>
public union BearerCharacter(SignedInCharacter, ActionResult);

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

	/// <summary>
	/// The signed-in account and the character its session acts as, read from the session itself, so a request made
	/// with one tab's bearer acts as that tab's character. Refused (409) when the session acts as no character.
	/// </summary>
	public async Task<BearerCharacter> ResolveCharacterAsync(ControllerBase controller)
		=> await ResolveAsync(controller) switch
		{
			SignedInAccount account => await ActingAsync(controller, account),
			ActionResult refused => refused,
		};

	private async Task<BearerCharacter> ActingAsync(ControllerBase controller, SignedInAccount account)
	{
		var characters = await accounts.GetCharactersAsync(account.Id);
		var acting = AccountSessionAuthenticationHandler.TryGetAccount(controller.User, out _, out _)
			? AccountSessionAuthenticationHandler.ActingCharacter(controller.User) is { } claimed
				? characters.FirstOrDefault(character => character.Object.DBRef.Matches(claimed))
				: null
			: await SessionOf(controller) is { } session ? ActingCharacterResolver.Resolve(session, characters) : null;

		return acting is null
			? controller.Conflict("This session is not playing a character; choose one first.")
			: new SignedInCharacter(account.Id, acting);
	}

	private async Task<IAccountSessionStore.SessionIdentity?> SessionOf(ControllerBase controller)
		=> controller.Request.Headers.Authorization.FirstOrDefault() is { } header
			&& header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
				? await sessions.ValidateAsync(header["Bearer ".Length..].Trim())
				: null;

	private static ObjectResult PasswordChangeRequired(ControllerBase controller)
		=> controller.StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action.");
}
