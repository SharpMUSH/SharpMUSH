using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// Intercepts requests flagged as bot traffic and serves pre-rendered HTML.
/// Must run AFTER <see cref="BotDetectionMiddleware"/> (which sets IsBot in Items).
///
/// For public wiki/character/scene routes:
///   1. Check pre-render cache; serve cached HTML if present.
///   2. Otherwise, look up the page via IWikiService, render to HTML, cache, serve.
///   3. Include canonical link + OpenGraph meta tags.
///
/// For anything else (or if the page doesn't exist): fall through to the SPA.
/// </summary>
public sealed class BotPrerenderMiddleware(
	RequestDelegate next,
	IWikiService wikiService,
	IPrerenderCacheService prerenderCache,
	ILogger<BotPrerenderMiddleware> logger)
{
	public async Task InvokeAsync(HttpContext context)
	{
		var isBot = context.Items[BotDetectionMiddleware.BotFlagKey] is true;

		if (!isBot)
		{
			await next(context);
			return;
		}

		var path = context.Request.Path.Value ?? "/";
		var scheme = context.Request.Scheme;
		var host = context.Request.Host.Value;
		var canonicalBase = $"{scheme}://{host}";

		// Try to serve from cache first
		var cached = prerenderCache.Get(path);
		if (cached is not null)
		{
			logger.LogDebug("BotPrerender: cache hit for {Path}", path);
			await WriteHtmlResponse(context, cached);
			return;
		}

		string? html = null;

		// /wiki/{slug} — wiki page
		if (path.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase))
		{
			var slug = path["/wiki/".Length..].Trim('/');
			if (!string.IsNullOrEmpty(slug))
			{
				var result = await wikiService.GetBySlugAsync(slug);
				if (result.IsT0)
				{
					var page = result.AsT0;
					html = WikiController.GeneratePrerenderHtml(page, $"{canonicalBase}/wiki/{page.Slug}");
				}
			}
		}
		// /character/{name} — character profile alias → Character namespace
		else if (path.StartsWith("/character/", StringComparison.OrdinalIgnoreCase))
		{
			var name = path["/character/".Length..].Trim('/');
			if (!string.IsNullOrEmpty(name))
			{
				var result = await wikiService.GetBySlugAsync(name, WikiNamespace.Character);
				if (result.IsT0)
				{
					var page = result.AsT0;
					html = WikiController.GenerateCharacterPrerenderHtml(page, $"{canonicalBase}/character/{name}");
				}
			}
		}
		// /help/{topic} — help pages live in the Help namespace
		else if (path.StartsWith("/help/", StringComparison.OrdinalIgnoreCase))
		{
			var topic = path["/help/".Length..].Trim('/');
			if (!string.IsNullOrEmpty(topic))
			{
				var result = await wikiService.GetBySlugAsync(topic, WikiNamespace.Help);
				if (result.IsT0)
				{
					var page = result.AsT0;
					html = WikiController.GeneratePrerenderHtml(page, $"{canonicalBase}/help/{topic}");
				}
			}
		}

		if (html is not null)
		{
			prerenderCache.Set(path, html);
			logger.LogDebug("BotPrerender: rendered and cached {Path}", path);
			await WriteHtmlResponse(context, html);
			return;
		}

		// No pre-rendered content available — fall through to normal pipeline
		await next(context);
	}

	private static Task WriteHtmlResponse(HttpContext context, string html)
	{
		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.StatusCode = StatusCodes.Status200OK;
		return context.Response.WriteAsync(html);
	}
}
