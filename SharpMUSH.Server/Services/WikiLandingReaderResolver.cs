using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Services;

/// <summary>Optional wiki authentication and fresh authority for the two landing-aware reads.</summary>
public sealed class WikiLandingReaderResolver(
	IAdministrativeCapabilityService capabilities, DelegatedPermissions delegated, IWikiAccessService access)
{
	public async Task<Found<ClaimsPrincipal>> AuthenticateAsync(HttpContext context)
	{
		if (OAuthBearerAuthenticationHandler.AppToken(context.Request) is null) return context.User;
		var authenticated = await context.AuthenticateAsync(OAuthBearerAuthenticationHandler.SchemeName);
		return authenticated.Succeeded && authenticated.Principal is { } principal ? principal : new NotFound();
	}

	public async Task<WikiReader> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var authenticated = user.Identities.Where(identity => identity.IsAuthenticated).ToArray();
		if (authenticated.Length == 0) return await access.AnonymousAsync(ct);
		if (authenticated is not [var identity]) return WikiReader.From([], null);
		var principal = new ClaimsPrincipal(identity);
		if (identity.FindFirst(ClaimTypes.NameIdentifier)?.Value is not { Length: > 0 } accountId)
			return WikiReader.From([], null);

		if (identity.AuthenticationType == OAuthBearerAuthenticationHandler.SchemeName)
		{
			if (OAuthBearerAuthenticationHandler.App(principal) is not { Character.IsObjid: true } app)
				return WikiReader.From([], null);
			return WikiReader.From(await delegated.GrantedUncachedAsync(accountId, app, ct), app.Character.ToString());
		}

		if (identity.AuthenticationType != AccountSessionAuthenticationHandler.SchemeName
			|| principal.HasClaim(AccountSessionAuthenticationHandler.MustChangePasswordClaim, "true"))
			return WikiReader.From([], null);
		CapabilityActor actor;
		string? dbref = null;
		if (identity.FindFirst(GameHub.CharacterDbrefClaim) is not null)
		{
			if (principal.GetActingCharacter() is not { IsObjid: true } character) return WikiReader.From([], null);
			actor = new CapabilityActor(accountId, character, character);
			dbref = character.ToString();
		}
		else actor = new CapabilityActor(accountId);
		return WikiReader.From(await capabilities.GetGrantedScopesAsync(actor, ct), dbref);
	}
}
