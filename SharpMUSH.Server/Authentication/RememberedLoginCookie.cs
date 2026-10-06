using Microsoft.AspNetCore.Http;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// The cookie a remembered login ("Remember me") lives in: a token from
/// <c>IAccountSessionStore.CreateRememberedLoginAsync</c> that a tab trades for a session of its own
/// (<c>POST api/auth/account-resume</c>) when it has none.
/// </summary>
/// <remarks>
/// <para>HttpOnly, so no script on the page can read it; tab sessions stay in sessionStorage, one per
/// tab, which is what lets two tabs on one account play two characters. Secure whatever scheme the server
/// itself sees: the browser judges that against its own address, and a proxy in front usually ends TLS.
/// SameSite=Strict, so another site's page cannot spend it.</para>
/// <para>Scoped to <c>/api</c>: only the trade and the sign-out need it, and nothing else should carry a
/// months-long credential.</para>
/// </remarks>
public static class RememberedLoginCookie
{
	public const string Name = "sharpmush_remember";

	/// <summary>How long a remembered login lasts past its last use.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

	/// <summary>The remembered login this request carries, if any.</summary>
	public static string? Read(HttpRequest request) =>
		request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrWhiteSpace(token) ? token : null;

	/// <summary>Sets (or renews) the cookie, valid for another <see cref="Lifetime"/>.</summary>
	public static void Write(HttpResponse response, string token) =>
		response.Cookies.Append(Name, token, Options(Lifetime));

	/// <summary>Tells the browser to drop the cookie.</summary>
	public static void Delete(HttpResponse response) =>
		response.Cookies.Delete(Name, Options(maxAge: null));

	private static CookieOptions Options(TimeSpan? maxAge) => new()
	{
		HttpOnly = true,
		Secure = true,
		SameSite = SameSiteMode.Strict,
		Path = "/api",
		MaxAge = maxAge,
		IsEssential = true,
	};
}
