using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
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
///
/// W-4: IWikiService may be registered Scoped. This middleware is registered as a
/// singleton-lifetime middleware in the pipeline.  Constructor-injecting a Scoped
/// service would create a captive dependency (the Scoped object lives forever).
/// Fix: inject IServiceScopeFactory and resolve IWikiService per-request.
/// </summary>
public sealed class BotPrerenderMiddleware(
	RequestDelegate next,
	IServiceScopeFactory scopeFactory,
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

		// A bot may request any locale, so the locale is part of the cache identity. Keying on path alone
		// would let the first French crawler poison the entry every other reader gets.
		// This is a read path, so the permissive form: an unparseable ?lang= keys the default entry rather
		// than producing an error.
		var requestedLang = context.Request.Query["lang"].FirstOrDefault();
		var normalizedLang = WikiHelpers.NormalizeLocaleOrEmpty(requestedLang);
		var cacheKey = normalizedLang.Length == 0 ? path : $"{path}#{normalizedLang}";

		var cached = prerenderCache.Get(cacheKey);
		if (cached is not null)
		{
			logger.LogDebug("BotPrerender: cache hit for {Key}", cacheKey);
			await WriteHtmlResponse(context, cached);
			return;
		}

		// W-4: Resolve IWikiService inside a per-request scope to avoid captive
		// dependency if IWikiService is registered as Scoped.
		await using var scope = scopeFactory.CreateAsyncScope();
		var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();
		var localization = scope.ServiceProvider.GetRequiredService<IWikiLocalizationService>();

		string? html = null;

		if (path.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase))
		{
			var segments = path["/wiki/".Length..].Trim('/').Split('/');
			if (segments.Length >= 3)
			{
				var ns = ParseNamespace(segments[0]);
				var category = segments[1];
				var slug = string.Join('/', segments[2..]);
				var result = await wikiService.GetBySlugAsync(slug, category, ns);
				// Published gate: a prerender is anonymous, bot-facing output, and this branch had no
				// visibility check at all — a draft page was reachable by any crawler that asked.
				if (result.IsT0 && result.AsT0.Published)
				{
					var page = result.AsT0;
					html = WikiController.GeneratePrerenderHtml(
						await localization.LocalizeAsync(page, normalizedLang, includeDrafts: false),
						$"{canonicalBase}/wiki/{page.Namespace}/{page.Category}/{page.Slug}",
						await localization.GetVisibleLocalesAsync(page, includeDrafts: false),
						localization.DefaultLocale);
				}
			}
		}
		else if (path.StartsWith("/character/", StringComparison.OrdinalIgnoreCase))
		{
			var name = path["/character/".Length..].Trim('/');
			if (!string.IsNullOrEmpty(name))
			{
				var result = await wikiService.GetBySlugAsync(name, WikiHelpers.DefaultCategory, WikiNamespace.Character);
				if (result.IsT0 && result.AsT0.Published)
				{
					var page = result.AsT0;
					html = WikiController.GenerateCharacterPrerenderHtml(
						await localization.LocalizeAsync(page, normalizedLang, includeDrafts: false),
						$"{canonicalBase}/character/{name}",
						await localization.GetVisibleLocalesAsync(page, includeDrafts: false),
						localization.DefaultLocale);
				}
			}
		}
		else if (path.StartsWith("/help/", StringComparison.OrdinalIgnoreCase))
		{
			// Help is the shipped helpfile corpus, not a wiki namespace: this used to look the topic
			// up as a wiki slug, which never matched anything on a fresh game. The admin corpus
			// (/help/admin/…) is deliberately absent — a crawler is anonymous, and `ahelp` is
			// wizard-only in game.
			// HttpRequest.Path is already percent-decoded, so "%20" has become a space by now.
			var topic = path["/help/".Length..].Trim('/');
			if (!string.IsNullOrEmpty(topic) && !topic.Contains('/'))
			{
				var resolver = scope.ServiceProvider.GetRequiredService<IHelpTopicResolver>();
				var resolution = await resolver.ResolveAsync(HelpCorpora.Help, topic);
				if (resolution.TryPickT0(out var entry, out _))
				{
					html = HelpController.GeneratePrerenderHtml(
						entry,
						$"{canonicalBase}{HelpController.PublicTopicHref(entry.Topic)}");
				}
			}
		}

		if (html is not null)
		{
			prerenderCache.Set(cacheKey, html);
			logger.LogDebug("BotPrerender: rendered and cached {Key}", cacheKey);
			await WriteHtmlResponse(context, html);
			return;
		}

		await next(context);
	}

	private static WikiNamespace ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	private static Task WriteHtmlResponse(HttpContext context, string html)
	{
		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.StatusCode = StatusCodes.Status200OK;
		return context.Response.WriteAsync(html);
	}
}
