using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Markdig.Syntax;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using System.Text;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Read-only REST access to the help files the server ships — the same corpus the telnet
/// <c>help</c> command answers from, resolved by the same <see cref="IHelpTopicResolver"/>.
/// <code>
///   GET /api/help                    — the general index entry plus every topic name
///   GET /api/help/entry?topic=@mail  — resolve one topic
///   GET /api/help/admin              — the ahelp index (Wizard/God only)
///   GET /api/help/admin/entry?topic= — resolve one admin topic (Wizard/God only)
/// </code>
/// </summary>
/// <remarks>
/// Help is deliberately <b>not</b> wiki content. The wiki holds the game's editable world content;
/// these files ship with the server and are edited in the repository, so importing them into the
/// wiki would put engine documentation behind the wiki's edit/revision/protection machinery and
/// let a game's local edits silently diverge from the engine they document. The portal reads them
/// straight from <c>TextFiles/</c> instead.
/// <para>
/// Topics are keyed by the markdown headers inside the files, never by filename — <c>@mail</c> is a
/// topic, <c>sharpmail</c> is only the file it happens to live in. Because topic names include
/// <c>@mail</c>, <c>getting started</c>, <c>%#</c> and <c>#-1 exception</c>, the topic travels as a
/// query-string value rather than a path segment: several of those cannot survive a URL path at all.
/// </para>
/// <para>
/// The admin corpus is gated at the same trust level the game gates it: <c>ahelp</c> answers
/// "This command is for administrators only." to a mortal, so these routes answer 403.
/// </para>
/// </remarks>
[ApiController]
[Route("api/help")]
// Not on the "public-api" limiter: that is the login throttle, and reading help — a click per topic —
// spent the same per-client budget as signing in. These are in-memory reads, like the wiki's.
[Produces("application/json")]
public sealed class HelpController(IHelpTopicResolver resolver) : ControllerBase
{
	/// <summary>A corpus index: its own front-page entry, plus every topic name it holds.</summary>
	public record HelpIndexDto(string Corpus, string? Topic, string? Html, IReadOnlyList<string> Topics);

	/// <summary>
	/// The outcome of resolving one topic. Exactly one of <paramref name="Topic"/> and a non-empty
	/// <paramref name="Candidates"/> is populated; an outright miss is a 404 instead.
	/// </summary>
	public record HelpEntryDto(
		string Corpus,
		string RequestedTopic,
		string? Topic,
		string? Markdown,
		string? Html,
		IReadOnlyList<string> Candidates)
	{
		public string? ArticleId { get; init; }
		public string? SectionId { get; init; }
		public string? CanonicalHref { get; init; }
	}

	/// <summary>Portal URL for a topic in the general corpus.</summary>
	public static string PublicTopicHref(string topic) => $"/help/{Uri.EscapeDataString(topic)}";

	/// <summary>Portal URL for a topic in the admin corpus.</summary>
	public static string AdminTopicHref(string topic) => $"/help/admin/{Uri.EscapeDataString(topic)}";

	[HttpGet]
	[AllowAnonymous]
	public Task<ActionResult<HelpIndexDto>> Index() =>
		BuildIndexAsync(HelpCorpora.Help, "help", PublicTopicHref);

	[HttpGet("entry")]
	[AllowAnonymous]
	public Task<ActionResult<HelpEntryDto>> Entry([FromQuery] string? topic) =>
		BuildEntryAsync(HelpCorpora.Help, topic, PublicTopicHref);

	/// <summary>Shared article manifest for documentation exporters. No separate content source.</summary>
	[HttpGet("articles")]
	[AllowAnonymous]
	public Task<IReadOnlyList<HelpArticle>> Articles() => BuildArticlesAsync(HelpCorpora.Help);

	/// <summary>
	/// The admin gate. Pinned to the account-session scheme rather than the default one: in
	/// Development the default is <c>DebugAuth</c>, which authenticates every request as God, and a
	/// gate that opens for anyone who points a browser at a dev server is not a gate. Requiring a
	/// real session here also makes the refusal testable.
	/// </summary>
	private const string AdminScheme = AccountSessionAuthenticationHandler.SchemeName;

	[HttpGet("admin")]
	[Authorize(AuthenticationSchemes = AdminScheme, Roles = "Wizard,God")]
	public Task<ActionResult<HelpIndexDto>> AdminIndex() =>
		BuildIndexAsync(HelpCorpora.Admin, "ahelp", AdminTopicHref);

	[HttpGet("admin/entry")]
	[Authorize(AuthenticationSchemes = AdminScheme, Roles = "Wizard,God")]
	public Task<ActionResult<HelpEntryDto>> AdminEntry([FromQuery] string? topic) =>
		BuildEntryAsync(HelpCorpora.Admin, topic, AdminTopicHref);

	[HttpGet("admin/articles")]
	[Authorize(AuthenticationSchemes = AdminScheme, Roles = "Wizard,God")]
	public Task<IReadOnlyList<HelpArticle>> AdminArticles() => BuildArticlesAsync(HelpCorpora.Admin);

	private async Task<IReadOnlyList<HelpArticle>> BuildArticlesAsync(string corpus)
	{
		var articles = new Dictionary<string, HelpArticle>(StringComparer.OrdinalIgnoreCase);
		foreach (var topic in await resolver.ListTopicsAsync(corpus))
		{
			if ((await resolver.GetExactAsync(corpus, topic))?.Article is { } article)
			{
				articles.TryAdd(article.Id, article);
			}
		}
		return articles.Values.ToList();
	}

	/// <summary>
	/// Bot-facing static HTML for one help entry, served by <c>BotPrerenderMiddleware</c> so
	/// crawlers see the text instead of an empty SPA shell. Help files carry no locale dimension,
	/// so unlike the wiki's prerender there are no hreflang alternates to emit.
	/// </summary>
	public static string GeneratePrerenderHtml(HelpEntry entry, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{entry.Topic} - {siteName} Help");
		var markdown = entry.Article?.Markdown ?? entry.Markdown;
		var description = HttpUtility.HtmlEncode(HelpHtmlRenderer.ExtractPlainText(markdown, 200));
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html lang=\"en\">");
		sb.AppendLine("<head>");
		sb.AppendLine("  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		sb.AppendLine($"  <meta name=\"description\" content=\"{description}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{title}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{description}\" />");
		sb.AppendLine("  <meta property=\"og:type\" content=\"article\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{canonical}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(entry.Topic)}</h1>");
		sb.AppendLine($"  {HelpHtmlRenderer.RenderToHtml(markdown, PublicTopicHref, entry.Article)}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}

	private async Task<ActionResult<HelpIndexDto>> BuildIndexAsync(
		string corpus,
		string indexTopic,
		Func<string, string> href)
	{
		var topics = await resolver.ListTopicsAsync(corpus);
		var index = await resolver.GetExactAsync(corpus, indexTopic);

		return Ok(new HelpIndexDto(
			corpus,
			index?.Topic,
			index is null ? null : HelpHtmlRenderer.RenderToHtml(index.Markdown, href),
			topics));
	}

	private async Task<ActionResult<HelpEntryDto>> BuildEntryAsync(
		string corpus,
		string? topic,
		Func<string, string> href)
	{
		if (string.IsNullOrWhiteSpace(topic))
		{
			return BadRequest("A topic is required.");
		}

		var resolution = await resolver.ResolveAsync(corpus, topic);

		if (resolution is HelpEntry entry)
		{
			var markdown = entry.Article?.Markdown ?? entry.Markdown;
			var targets = Markdig.Markdown.Parse(markdown,
				RecursiveMarkdownHelper.ConfigureHelpSyntax(new Markdig.MarkdownPipelineBuilder()).Build())
				.Descendants<Markdig.Syntax.Inlines.LinkInline>()
				.Where(link => link.Url?.StartsWith("help ", StringComparison.Ordinal) == true)
				.Select(link => link.Url![5..]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var target in targets)
			{
				var resolved = await resolver.GetExactAsync(corpus, target);
				links[target] = resolved?.Article is { } article
					? href(article.Lookup) + (resolved.SectionId is null ? string.Empty : "#" + resolved.SectionId)
					: href(resolved?.Topic ?? target);
			}
			return Ok(new HelpEntryDto(
				corpus,
				topic,
				entry.Topic,
				markdown,
				HelpHtmlRenderer.RenderToHtml(markdown, target => links.GetValueOrDefault(target, href(target)), entry.Article),
				[])
			{
				ArticleId = entry.Article?.Id,
				SectionId = entry.SectionId,
				CanonicalHref = href(entry.Article?.Lookup ?? entry.Topic)
					+ (entry.SectionId is null ? string.Empty : "#" + entry.SectionId)
			});
		}

		// Several topics matched. That is an answer, not a failure — the reader picks one — so it
		// is a 200 with the candidate list rather than a 404 the portal would have to special-case.
		if (resolution is HelpCandidates candidates)
		{
			return Ok(new HelpEntryDto(corpus, topic, null, null, null, candidates.Topics));
		}

		return NotFound(new HelpEntryDto(corpus, topic, null, null, null, []));
	}
}
