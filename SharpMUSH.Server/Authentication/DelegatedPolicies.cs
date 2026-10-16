using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// The policies of the endpoints an app may call with its access token: <c>[Authorize(Policy =
/// DelegatedPolicies.Commands)]</c>. Each authenticates with the default scheme (a player's own session) and with
/// <see cref="OAuthBearerAuthenticationHandler"/>, and needs the scope it is named for when the caller is an app. A
/// permission policy (<see cref="PermissionPolicyProvider"/>) takes an app's token too, when the token's scopes cover
/// the permission. An endpoint that names neither never sees an app's token.
/// </summary>
public static class DelegatedPolicies
{
	public const string Prefix = "delegated:";

	/// <summary>Run commands as the character: <c>api/commands</c>.</summary>
	public const string Commands = Prefix + DelegatedScopes.Commands;

	/// <summary>Say who the character is: <c>api/oauth/me</c>.</summary>
	public const string Profile = Prefix + DelegatedScopes.Profile;

	/// <summary>The default scheme (a player's own session), then <see cref="OAuthBearerAuthenticationHandler"/>.</summary>
	public static string[] Schemes(AuthenticationOptions authentication)
		=> (authentication.DefaultAuthenticateScheme ?? authentication.DefaultScheme) is { } defaultScheme
			? [defaultScheme, OAuthBearerAuthenticationHandler.SchemeName]
			: [OAuthBearerAuthenticationHandler.SchemeName];

	/// <summary>The policy named <paramref name="policyName"/>, when it is one of these.</summary>
	public static AuthorizationPolicy? Build(string policyName, AuthenticationOptions authentication)
	{
		if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)
				|| policyName[Prefix.Length..] is not { } scope || !DelegatedScopes.IsKnown(scope)) return null;
		return new AuthorizationPolicyBuilder(Schemes(authentication))
			.RequireAuthenticatedUser()
			.AddRequirements(new DelegatedScopeRequirement(scope))
			.Build();
	}
}

/// <summary>An app's token must carry <see cref="Scope"/>; a player's own session needs nothing more.</summary>
public sealed class DelegatedScopeRequirement(string scope) : IAuthorizationRequirement
{
	public string Scope { get; } = scope;
}

public sealed class DelegatedScopeAuthorizationHandler : AuthorizationHandler<DelegatedScopeRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DelegatedScopeRequirement requirement)
	{
		var app = context.User.Identities.FirstOrDefault(identity =>
			identity is { IsAuthenticated: true, AuthenticationType: OAuthBearerAuthenticationHandler.SchemeName });
		if (app is null || app.HasClaim(OAuthBearerAuthenticationHandler.ScopeClaim, requirement.Scope))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}
}
