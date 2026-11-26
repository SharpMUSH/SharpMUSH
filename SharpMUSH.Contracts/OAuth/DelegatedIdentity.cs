using System.Security.Claims;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// How a request made with an app's access token is told apart: the authentication scheme that reads the token and
/// the claims it puts on the caller. The host's bearer handler writes them; a plugin endpoint that lets apps in reads
/// them here rather than depending on the host's types.
/// </summary>
public static class DelegatedIdentity
{
	/// <summary>The authentication scheme of an app's access token.</summary>
	public const string SchemeName = "OAuth";

	/// <summary>The app's client id.</summary>
	public const string ClientClaim = "oauth_client";

	/// <summary>The app's name.</summary>
	public const string AppNameClaim = "oauth_app";

	/// <summary>One per scope the token carries.</summary>
	public const string ScopeClaim = "oauth_scope";

	/// <summary>The identity an app's access token authenticated on <paramref name="user"/>, if any.</summary>
	public static ClaimsIdentity? App(ClaimsPrincipal? user)
		=> user?.Identities.FirstOrDefault(identity => identity is { IsAuthenticated: true, AuthenticationType: SchemeName });

	/// <summary>Whether <paramref name="user"/> is an app that the player allowed <paramref name="scope"/>.</summary>
	public static bool AppHas(ClaimsPrincipal? user, string scope)
		=> App(user) is { } app && app.HasClaim(ScopeClaim, scope);
}
