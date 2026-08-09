using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
[EnableRateLimiting("public-api")]
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
		IReadOnlyList<string> Candidates);

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

	/// <summary>
	/// Bot-facing static HTML for one help entry, served by <c>BotPrerenderMiddleware</c> so
	/// crawlers see the text instead of an empty SPA shell. Help files carry no locale dimension,
	/// so unlike the wiki's prerender there are no hreflang alternates to emit.
	/// </summary>
	public static string GeneratePrerenderHtml(HelpEntry entry, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{entry.Topic} - {siteName} Help");
		var description = HttpUtility.HtmlEncode(HelpHtmlRenderer.ExtractPlainText(entry.Markdown, 200));
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
		sb.AppendLine($"  {HelpHtmlRenderer.RenderToHtml(entry.Markdown, PublicTopicHref)}");
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

		if (resolution.TryPickT0(out var entry, out var notAnEntry))
		{
			return Ok(new HelpEntryDto(
				corpus,
				topic,
				entry.Topic,
				entry.Markdown,
				HelpHtmlRenderer.RenderToHtml(entry.Markdown, href),
				[]));
		}

		// Several topics matched. That is an answer, not a failure — the reader picks one — so it
		// is a 200 with the candidate list rather than a 404 the portal would have to special-case.
		if (notAnEntry.TryPickT0(out var candidates, out _))
		{
			return Ok(new HelpEntryDto(corpus, topic, null, null, null, candidates.Topics));
		}

		return NotFound(new HelpEntryDto(corpus, topic, null, null, null, []));
	}
}
