using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Markdig extension that turns custom-container blocks (<c>::: name arg</c>)
/// with a recognised directive name into client-side placeholder divs that the
/// portal hydrates with live data at display time:
/// <list type="bullet">
///   <item><c>::: category lore</c> — pages in a category</item>
///   <item><c>::: pagelist help</c> — pages in a namespace</item>
///   <item><c>::: recent 10</c> — recently updated pages (count clamped 1–50)</item>
///   <item><c>::: facts pf2e:tradition=arcane, show=pf2e:level</c> — the pages a fact query finds, as
///   <c>wikiquery()</c> takes its conditions; a query, never softcode</item>
/// </list>
/// Each directive renders as
/// <c>&lt;div class="wiki-directive" data-directive="…" data-arg="…"&gt;&lt;/div&gt;</c>.
/// Arguments are validated against a strict whitelist; invalid directives render
/// nothing. Containers with any other info string keep the default Markdig
/// custom-container rendering.
/// </summary>
public sealed class WikiDirectiveExtension : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		// No parser changes required — UseAdvancedExtensions already registers the
		// custom-container (:::) block parser. Only rendering is customised.
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is not HtmlRenderer htmlRenderer)
			return;

		// Find and replace the default custom-container renderer with our subclass.
		var existing = htmlRenderer.ObjectRenderers.FindExact<HtmlCustomContainerRenderer>();
		if (existing is not null)
			htmlRenderer.ObjectRenderers.Remove(existing);

		htmlRenderer.ObjectRenderers.Insert(0, new WikiDirectiveContainerRenderer());
	}
}

/// <summary>
/// The live listing directives a wiki page's text may hold, and the arguments each accepts. The portal and the
/// game read the same rules, so a block one of them shows is a block the other shows.
/// </summary>
public static partial class WikiDirectives
{
	/// <summary>Maximum page count accepted by the <c>recent</c> directive.</summary>
	public const int MaxRecentCount = 50;

	/// <summary>Directive arguments: letters, digits, spaces, underscores, hyphens; max 64 chars.</summary>
	[GeneratedRegex(@"^[a-zA-Z0-9 _-]{0,64}$")]
	private static partial Regex ArgPattern();

	/// <summary>The <c>recent</c> argument: digits only.</summary>
	[GeneratedRegex(@"^\d{1,9}$")]
	private static partial Regex RecentArgPattern();

	/// <summary>The longest <c>facts</c> argument: four conditions and the settings a query takes fit well within it.</summary>
	private const int MaxFactsLength = 1024;

	/// <summary>Whether <paramref name="directive"/> names a live listing.</summary>
	public static bool IsDirective(string directive)
		=> directive.ToLowerInvariant() is "category" or "pagelist" or "recent" or "facts";

	/// <summary>
	/// <paramref name="arg"/> as <paramref name="directive"/> takes it (a <c>recent</c> count clamped to 1–50), or
	/// null when it is not one the directive accepts.
	/// </summary>
	public static string? Argument(string directive, string arg)
	{
		if (directive.Equals("recent", StringComparison.OrdinalIgnoreCase))
		{
			if (!RecentArgPattern().IsMatch(arg) || !int.TryParse(arg, out var count))
				return null;
			return Math.Clamp(count, 1, MaxRecentCount).ToString();
		}

		// A fact query is checked when it is answered, where its reason can be shown; here it only has to be one line.
		if (directive.Equals("facts", StringComparison.OrdinalIgnoreCase))
			return arg.Length is > 0 and <= MaxFactsLength && !arg.Any(char.IsControl) ? arg : null;

		return ArgPattern().IsMatch(arg) ? arg : null;
	}
}

/// <summary>
/// Replaces the default <see cref="HtmlCustomContainerRenderer"/> so that
/// containers whose info string is a known wiki directive emit a placeholder
/// <c>div.wiki-directive</c> instead of the default container markup.
/// All other custom containers delegate to the base (default) rendering.
/// </summary>
internal sealed class WikiDirectiveContainerRenderer : HtmlCustomContainerRenderer
{
	protected override void Write(HtmlRenderer renderer, CustomContainer container)
	{
		var directive = container.Info?.Trim().ToLowerInvariant();

		if (directive is null || !WikiDirectives.IsDirective(directive))
		{
			// Unknown info string — keep the default custom-container rendering.
			base.Write(renderer, container);
			return;
		}

		var arg = WikiDirectives.Argument(directive, container.Arguments?.Trim() ?? string.Empty);
		if (arg is null)
			return; // Invalid argument — render nothing for this container.

		renderer.EnsureLine();
		if (renderer.EnableHtmlForBlock)
		{
			renderer.Write("<div class=\"wiki-directive\" data-directive=\"");
			renderer.Write(directive);
			renderer.Write("\" data-arg=\"");
			renderer.WriteEscape(arg);
			renderer.Write("\"></div>");
		}
		renderer.EnsureLine();
	}
}
