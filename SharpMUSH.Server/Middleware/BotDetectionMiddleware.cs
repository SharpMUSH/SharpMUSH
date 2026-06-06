using Microsoft.AspNetCore.Http;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// Detects bot/crawler user agents and sets a feature flag on the HttpContext so that
/// downstream handlers can serve pre-rendered HTML instead of the Blazor WASM bundle.
///
/// Protected (authenticated) routes always receive 403 for bot requests.
/// Public routes are handled by <see cref="BotPrerenderMiddleware"/>.
/// </summary>
public sealed class BotDetectionMiddleware(RequestDelegate next)
{
	public const string BotFlagKey = "IsBot";

	/// <summary>
	/// Known bot user-agent substrings (case-insensitive).
	/// Extend this list as needed.
	/// </summary>
	private static readonly string[] BotSubstrings =
	[
		"googlebot", "bingbot", "slurp", "duckduckbot", "baiduspider",
		"yandexbot", "sogou", "facebot", "facebookexternalhit",
		"linkedinbot", "twitterbot", "discordbot", "telegrambot",
		"whatsapp", "applebot", "ia_archiver", "archive.org_bot",
		"curl/", "wget/", "python-requests", "go-http-client",
		"_escaped_fragment_",   // legacy AJAX crawling convention (query param marker)
	];

	/// <summary>Routes that require authentication and must return 403 for bots.</summary>
	private static readonly string[] AuthenticatedPrefixes =
	[
		"/mail", "/play", "/scenes/", "/settings", "/admin",
	];

	public async Task InvokeAsync(HttpContext context)
	{
		var ua = context.Request.Headers.UserAgent.ToString();
		var isBot = IsBot(ua, context.Request.Query);

		context.Items[BotFlagKey] = isBot;

		if (isBot)
		{
			var path = context.Request.Path.Value ?? "/";
			foreach (var prefix in AuthenticatedPrefixes)
			{
				if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					// Bots must not see authenticated content – even a live scene detail or settings page.
					context.Response.StatusCode = StatusCodes.Status403Forbidden;
					await context.Response.WriteAsync("Forbidden");
					return;
				}
			}
		}

		await next(context);
	}

	/// <summary>
	/// Returns true when the user-agent string or query string suggests a bot.
	/// </summary>
	public static bool IsBot(string userAgent, IQueryCollection query)
	{
		if (query.ContainsKey("_escaped_fragment_"))
			return true;

		if (string.IsNullOrEmpty(userAgent))
			return false;

		foreach (var sub in BotSubstrings)
		{
			if (userAgent.Contains(sub, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}
