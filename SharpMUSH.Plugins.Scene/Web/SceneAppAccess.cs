using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// Lets an app the player allowed <see cref="DelegatedScopes.ScenesRead"/> read scenes as the character it acts for.
/// The scene reads are open to anyone and read the caller from the default scheme (a player's own session), which
/// ignores an app's token; this filter reads the token instead, when the request carries one:
/// <list type="bullet">
///   <item>no app token: nothing changes;</item>
///   <item>a token that is invalid, expired or revoked: 401, so the app refreshes it;</item>
///   <item>a valid token without <see cref="DelegatedScopes.ScenesRead"/>: 403 <c>insufficient_scope</c>;</item>
///   <item>otherwise the request reads as the token's character, through the same visibility rule a player's session
///   gets (<see cref="SceneVisibility"/>).</item>
/// </list>
/// Only read endpoints carry it. The drafts and calendar links (<see cref="ScenePortalController"/>) never see an app.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class SceneAppReadAttribute : Attribute, IAsyncAuthorizationFilter
{
	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		var http = context.HttpContext;
		if (!SceneAppAccess.CarriesAppToken(http.Request)) return;

		var result = await http.AuthenticateAsync(DelegatedIdentity.SchemeName);
		if (!result.Succeeded || result.Principal is not { } principal)
		{
			context.Result = new ChallengeResult(DelegatedIdentity.SchemeName);
			return;
		}

		if (!DelegatedIdentity.AppHas(principal, DelegatedScopes.ScenesRead))
		{
			context.Result = new ForbidResult(DelegatedIdentity.SchemeName);
			return;
		}

		http.User = principal;
	}
}

/// <summary>What an app's access token changes about reading scenes.</summary>
internal static class SceneAppAccess
{
	/// <summary>Whether the request's <c>Authorization</c> header holds an app's access token.</summary>
	internal static bool CarriesAppToken(Microsoft.AspNetCore.Http.HttpRequest request)
		=> request.Headers.Authorization.FirstOrDefault() is { } header
			&& header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			&& OAuthService.IsAccessToken(header["Bearer ".Length..].Trim());

	/// <summary>
	/// Whether <paramref name="user"/> may have the wizard's view of every scene, hidden ones too, when its character
	/// has it. An app needs <see cref="DelegatedScopes.Game"/> for that, as for every other staff power; a player's
	/// own session needs nothing more.
	/// </summary>
	internal static bool MaySeeEvery(ClaimsPrincipal? user)
		=> DelegatedIdentity.App(user) is null || DelegatedIdentity.AppHas(user, DelegatedScopes.Game);
}
