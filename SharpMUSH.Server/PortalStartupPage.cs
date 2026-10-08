using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server;

/// <summary>
/// Until this server has first been <see cref="ServerReadiness.IsReady">ready</see>, a browser asking for the
/// portal gets a small "Game is starting up" page instead of <c>index.html</c>.
/// </summary>
/// <remarks>
/// <para>The check lives here, once, so the portal itself carries no startup logic: a browser only ever
/// downloads the WebAssembly app from a server that can play the game. The page is a 503 with
/// <c>Retry-After</c>, fetches nothing but <c>/api/health</c>, and reloads itself into the portal when that
/// answers 200 (with a meta refresh for a browser without script).</para>
/// <para>Only the SPA shell is held back — <c>/</c>, <c>/index.html</c> and the fallback routes. The API, hubs
/// and assets answer as they always did.</para>
/// </remarks>
public static class PortalStartupPage
{
	/// <summary>The two lines the page shows, per portal locale. Mirrors the client's <c>WidGameStartingUp</c> /
	/// <c>WidWaitingForServer</c> resx values, which remain the translators' source; a test keeps them in step.</summary>
	public static IReadOnlyDictionary<string, (string Title, string Detail)> Messages { get; } =
		new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
		{
			["en"] = ("Game is starting up…", "Waiting for the server to come online…"),
			["bg"] = ("Играта се стартира…", "Изчакване сървърът да стане достъпен…"),
			["da"] = ("Spillet starter op…", "Venter på, at serveren kommer online…"),
			["de"] = ("Das Spiel startet…", "Warten, bis der Server online ist…"),
			["es"] = ("El juego se está iniciando…", "Esperando a que el servidor esté en línea…"),
			["fr"] = ("Le jeu démarre…", "En attente de la mise en ligne du serveur…"),
			["hr"] = ("Igra se pokreće…", "Čekanje da se poslužitelj pokrene…"),
			["hu"] = ("A játék indul…", "Várakozás a szerver elindulására…"),
			["nb"] = ("Spillet starter opp…", "Venter på at serveren skal komme på nett…"),
			["nl"] = ("Het spel start op…", "Wachten tot de server online komt…"),
			["pl"] = ("Gra się uruchamia…", "Oczekiwanie na uruchomienie serwera…"),
			["pt-BR"] = ("O jogo está iniciando…", "Aguardando o servidor ficar online…"),
			["ro"] = ("Jocul pornește…", "Se așteaptă pornirea serverului…"),
			["ru"] = ("Игра запускается…", "Ожидание запуска сервера…"),
			["sv"] = ("Spelet startar…", "Väntar på att servern ska starta…"),
			["zh-Hans"] = ("游戏正在启动…", "正在等待服务器上线…"),
		};

	/// <summary>How long a browser (or crawler) is told to wait before asking again.</summary>
	public const int RetryAfterSeconds = 3;

	/// <summary>
	/// Holds back the portal shell while the server is not yet ready. Place after routing (it reads the
	/// matched endpoint) and before the static-file middleware.
	/// </summary>
	public static IApplicationBuilder UsePortalStartupPage(this IApplicationBuilder app) =>
		app.Use(async (context, next) =>
		{
			if (!IsShellRequest(context)
				|| context.RequestServices.GetRequiredService<ServerReadiness>().HasBeenReady)
			{
				await next(context);
				return;
			}

			var current = context.RequestServices.GetService<IOptionsWrapper<SharpMUSHOptions>>()?.CurrentValue;
			context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
			context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
			context.Response.Headers.CacheControl = "no-store";
			context.Response.ContentType = "text/html; charset=utf-8";
			await context.Response.WriteAsync(Render(current?.Net.MudName, current?.Cosmetic));
		});

	/// <summary>
	/// A request for the portal's HTML shell: the SPA fallback (any non-file route that no API or hub claims),
	/// or <c>/index.html</c> itself. Everything else — API, hubs, <c>/_framework</c>, assets — passes.
	/// </summary>
	internal static bool IsShellRequest(HttpContext context)
	{
		if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) return false;

		var path = context.Request.Path;
		if (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)) return true;

		return context.GetEndpoint()?.Metadata.GetMetadata<PortalShellEndpoint>() is not null;
	}

	/// <summary>Marks the endpoint that serves the SPA shell (see <see cref="PortalStaticFiles.MapPortal"/>).</summary>
	public sealed class PortalShellEndpoint;

	/// <summary>
	/// The startup page for <paramref name="gameName"/>, in English until its script picks the visitor's locale,
	/// with the game's <c>portal_logo</c> and tab icon when <paramref name="cosmetic"/> names them.
	/// </summary>
	public static string Render(string? gameName, CosmeticOptions? cosmetic = null)
	{
		var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(gameName) ? "SharpMUSH" : gameName);
		var logo = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(cosmetic?.PortalLogo) ? PortalPicture.DefaultLogo : cosmetic.PortalLogo.Trim());
		var favicon = WebUtility.HtmlEncode(cosmetic is null ? PortalPicture.DefaultLogo : PortalPicture.Favicon(cosmetic));
		var messages = JsonSerializer.Serialize(Messages.ToDictionary(m => m.Key, m => new[] { m.Value.Title, m.Value.Detail }));
		var (title, detail) = Messages["en"];

		// Picks the locale the portal would boot in (the stored choice, else the browser's), falling back to
		// English. Then asks /api/health until it answers 200 and reloads into the portal.
		return $$$"""
			<!DOCTYPE html>
			<html lang="en">
			<head>
			<meta charset="utf-8" />
			<meta name="viewport" content="width=device-width, initial-scale=1.0" />
			<meta http-equiv="refresh" content="{{{RetryAfterSeconds * 5}}}" />
			<title>{{{name}}}</title>
			<link rel="icon" href="{{{favicon}}}" />
			<style>
			html,body{margin:0;height:100%;background:#0e0f11;color:#e9edf0;font-family:"Hanken Grotesk",system-ui,sans-serif}
			main{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1.25rem;text-align:center;padding:1rem;box-sizing:border-box;background:radial-gradient(ellipse at center,rgba(0,245,183,.12),transparent 60%)}
			img{width:6rem;height:6rem}
			h1{margin:0;font-size:1.75rem;font-weight:600;color:#00f5b7}
			p{margin:0;color:#9aa3ab}
			.spin{width:2.5rem;height:2.5rem;border:3px solid rgba(0,245,183,.25);border-top-color:#00f5b7;border-radius:50%;animation:s 1s linear infinite}
			@keyframes s{to{transform:rotate(360deg)}}
			@media (prefers-reduced-motion:reduce){.spin{animation:none}}
			</style>
			</head>
			<body>
			<main role="status" aria-live="polite">
			<img src="{{{logo}}}" alt="" />
			<h1>{{{name}}}</h1>
			<div class="spin" aria-hidden="true"></div>
			<p id="title">{{{title}}}</p>
			<p id="detail">{{{detail}}}</p>
			</main>
			<script>
			(() => {
			  const messages = {{{messages}}};
			  let locale = null;
			  try { locale = localStorage.getItem('locale'); } catch {}
			  const pick = [locale, ...(navigator.languages || [])].find(l => l && (messages[l] || messages[l.split('-')[0]]));
			  const m = pick && (messages[pick] || messages[pick.split('-')[0]]);
			  if (m) {
			    document.documentElement.lang = pick;
			    document.getElementById('title').textContent = m[0];
			    document.getElementById('detail').textContent = m[1];
			  }
			  const poll = async () => {
			    try {
			      const r = await fetch('/api/health', { cache: 'no-store' });
			      if (r.ok) { location.reload(); return; }
			    } catch {}
			    setTimeout(poll, {{{RetryAfterSeconds * 1000}}});
			  };
			  setTimeout(poll, {{{RetryAfterSeconds * 1000}}});
			})();
			</script>
			</body>
			</html>
			""";
	}
}
