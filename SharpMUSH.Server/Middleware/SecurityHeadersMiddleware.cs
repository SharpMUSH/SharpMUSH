using Microsoft.AspNetCore.Http;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// The browser hardening headers every response from the portal, the API and the hubs carries:
/// no framing by another site, no MIME sniffing, no full URL in the Referer sent elsewhere, and no
/// plugins or rebased relative URLs. A header a controller already set (a stricter Referrer-Policy,
/// say) is kept. <c>/http/</c> is left alone: those responses are the game's softcode, which sets
/// its own headers, as PennMUSH's do.
/// </summary>
/// <remarks>
/// The policy carries no <c>script-src</c>: a hosted application's sandboxed <c>srcdoc</c> frame inherits
/// it, and that frame runs the package's inline scripts.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
	public const string ContentSecurityPolicy = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
	public const string FrameOptions = "SAMEORIGIN";
	public const string ReferrerPolicy = "strict-origin-when-cross-origin";

	public Task InvokeAsync(HttpContext context)
	{
		if (!context.Request.Path.StartsWithSegments("/http"))
		{
			context.Response.OnStarting(() =>
			{
				var headers = context.Response.Headers;
				headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
				headers.TryAdd("X-Frame-Options", FrameOptions);
				headers.TryAdd("X-Content-Type-Options", "nosniff");
				headers.TryAdd("Referrer-Policy", ReferrerPolicy);
				return Task.CompletedTask;
			});
		}

		return next(context);
	}
}
