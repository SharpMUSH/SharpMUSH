using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// A theme's own stylesheet: CSS that staff write to change anything the colour and style choices do not reach. It is
/// laid after the theme's tokens, so it wins over them. Saving checks it (<see cref="Validate"/>): it may not load
/// anything from another site, run script, or close the <c>&lt;style&gt;</c> element it is written into. Colour tokens it
/// sets in <c>:root</c> count as the theme's own (<see cref="ColorOverrides"/>), so contrast is still checked.
/// </summary>
public static partial class ThemeStylesheet
{
	/// <summary>The longest stylesheet a theme may carry, in characters.</summary>
	public const int MaxLength = 64 * 1024;

	/// <summary>The image types a <c>data:</c> URL in a stylesheet may hold.</summary>
	public static readonly IReadOnlyList<string> DataImageTypes = ["png", "jpeg", "gif", "webp", "avif", "svg+xml"];

	/// <summary>A selector a stylesheet can rely on, the part of the portal it reaches, and the section it belongs in.</summary>
	public sealed record Hook(string Section, string Selector, string What);

	/// <summary>
	/// The parts of the portal a stylesheet can address, by section, in the order <see cref="Starter"/> writes them.
	/// Component classes are doubled (<c>.kit-card.kit-card</c>) because a component's own scoped rules carry an
	/// attribute as well as the class, and a rule here needs the same weight to win.
	/// </summary>
	public static readonly IReadOnlyList<Hook> Hooks =
	[
		new("Shell", ".phosphor-shell", "The whole page behind everything: background and texture."),
		new("Shell", ".phosphor-rail", "The icon rail on the far left."),
		new("Shell", ".phosphor-sidebar", "The main navigation sidebar."),
		new("Shell", ".phosphor-topbar", "The bar along the top of the page."),
		new("Shell", ".phosphor-pagebar", "A section's own sidebar (Wiki, Settings, Admin...)."),
		new("Shell", ".phosphor-main", "The page area to the right of the sidebars."),
		new("Shell", ".phosphor-bottomnav", "The tabs along the bottom on a phone."),
		new("Shell", ".phosphor-terminal", "The docked terminal."),
		new("Cards", ".kit-card.kit-card", "Every card: background, border, corners, shadow."),
		new("Cards", ".kit-card-head.kit-card-head", "A card's header row."),
		new("Cards", ".kit-card-title.kit-card-title", "A card's title."),
		new("Cards", ".kit-link-card.kit-link-card", "The linked cards on overview pages."),
		new("Page headers", ".kit-page-head.kit-page-head", "A page's header block."),
		new("Page headers", ".kit-page-kicker.kit-page-kicker", "The small line above a page title."),
		new("Page headers", ".kit-page-title.kit-page-title", "A page's title."),
		new("Page headers", ".kit-page-desc.kit-page-desc", "The line under a page title."),
		new("Content", ".wiki-content", "Rendered wiki pages."),
		new("Content", ".mush-help-md", "Rendered help."),
		new("Controls", ".mud-button-root", "Every button."),
		new("Controls", ".mud-input", "Text fields and selects."),
		new("Controls", ".mud-chip", "Chips and tags."),
		new("Controls", ".mud-table", "Tables."),
	];

	/// <summary>
	/// The custom properties <see cref="Starter"/> lists, by group, with what each does. A colour token's value comes
	/// from the theme; the rest come from it when it sets them and from <c>tokens.css</c> otherwise.
	/// </summary>
	public static readonly IReadOnlyList<(string Group, IReadOnlyList<(string Name, string What)> Properties)> Variables =
	[
		("Surfaces", [
			(ThemeTokens.Background, "The page."),
			(ThemeTokens.Surface, "Cards and panels."),
			(ThemeTokens.Surface2, "Raised rows and the current sidebar row."),
			(ThemeTokens.Surface3, "Sidebars and the top bar."),
			(ThemeTokens.Rail, "The icon rail."),
			("card-bg", "A card's background; may be a gradient or layered images."),
			("code-bg", "Code blocks, help examples and the softcode console."),
		]),
		("Text", [
			(ThemeTokens.Text, "Body text."),
			(ThemeTokens.TextDim, "Secondary text."),
			(ThemeTokens.TextFaint, "Hints and timestamps."),
			("title-color", "Page and card titles."),
			("code-text", "Text in code blocks."),
		]),
		("Lines", [
			(ThemeTokens.Border, "Borders and table lines."),
			(ThemeTokens.BorderSoft, "Dividers."),
		]),
		("Accent", [
			(ThemeTokens.Accent, "Links, the current item, primary buttons. A character's own accent replaces it."),
			("accent-dim", "The accent's quieter shade."),
			("accent-on", "Text on an accent fill."),
		]),
		("Status", [
			(ThemeTokens.Warn, "Unsaved and changed."),
			(ThemeTokens.LinkMissing, "Wiki links to pages that do not exist, and alerts."),
			("danger", "Deletes, denials and errors."),
			("success", "Allows and results."),
			("info", "Notices."),
			("special", "Roles, forms and other marked kinds."),
		]),
		("Syntax", [
			("syntax-command", "Softcode: $-command patterns."),
			("syntax-function", "Softcode: function names."),
			("syntax-substitution", "Softcode: %-substitutions."),
			("syntax-dbref", "Softcode: #dbrefs."),
			("syntax-reference", "Softcode: attribute references."),
			("syntax-at-command", "Softcode: @-commands."),
			("syntax-danger", "Softcode: risky code."),
			("syntax-string", "Help: inline code."),
			("syntax-link", "Help: links to other topics."),
			("syntax-heading", "Help: headings."),
			("syntax-emphasis", "Help: bold text."),
		]),
		("Shape", [
			("radius", "Small corners: buttons, inputs."),
			("radius-lg", "Large corners: menus, rail items."),
			("radius-card", "Cards and banners."),
			("radius-row", "Sidebar rows and tiles."),
			("card-border-style", "A card's border style."),
			("card-border-width", "A card's border width."),
			("card-shadow", "A card's shadow."),
			("shadow", "The general raised shadow."),
		]),
		("Type", [
			("font-ui", "Body and controls."),
			("font-display", "Titles."),
			("font-mono", "The terminal and code."),
			("font-title-weight", "Titles' weight."),
			("title-transform", "Titles' case: none or uppercase."),
			("title-tracking", "Titles' letter spacing."),
			("title-style", "Titles' style: normal or italic."),
		]),
		("Decoration", [
			("texture", "The page texture, layered over the background."),
			("texture-size", "The texture's background-size."),
			("ornament-before", "A glyph before page titles."),
			("ornament-after", "A glyph after page titles."),
			("title-underline", "The rule under page titles."),
			("title-shadow", "Titles' glow or emboss."),
			("image-filter", "The tone banners and covers take."),
		]),
	];

	// tokens.css's values for the properties a resolved theme does not set itself.
	private static readonly IReadOnlyDictionary<string, string> SheetDefaults = new Dictionary<string, string>
	{
		["radius-lg"] = "14px",
		["radius-card"] = "16px",
		["radius-row"] = "10px",
		["font-mono"] = "\"Cascadia Mono\", \"DejaVu Sans Mono\", monospace",
		["font-title-weight"] = "700",
	};

	/// <summary>
	/// Problems with <paramref name="css"/>: too long, holding HTML, loading from another site, running script, or with
	/// braces that do not balance. Empty when it can be saved. Escapes are read before checking, so a disguised
	/// <c>url(</c> or <c>@import</c> is found too.
	/// </summary>
	public static IReadOnlyList<string> Validate(string? css)
	{
		var problems = new List<string>();
		if (string.IsNullOrEmpty(css))
		{
			return problems;
		}

		if (css.Length > MaxLength)
		{
			problems.Add($"A stylesheet is at most {MaxLength} characters; this one is {css.Length}.");
			return problems;
		}

		if (!TryStripComments(css, out var bare))
		{
			problems.Add("A comment is not closed: every /* needs its */.");
			return problems;
		}

		var text = Unescape(bare).ToLowerInvariant();
		if (text.Contains('<'))
		{
			problems.Add("A stylesheet holds CSS only: '<' is not allowed, even in a string. In a data: URL, write it as %3C.");
		}

		problems.AddRange(((string[])["@import", "@charset", "@namespace"])
			.Where(rule => text.Contains(rule, StringComparison.Ordinal))
			.Select(rule => $"{rule} is not allowed: a theme's stylesheet is one sheet, read as written."));
		problems.AddRange(Forbidden.Where(f => f.Pattern.IsMatch(text)).Select(f => $"{f.What} is not allowed."));

		var urls = UrlPattern().Matches(text);
		if (urls.Count != Regex.Matches(text, @"\burl\(").Count)
		{
			problems.Add("A url( is not closed.");
		}

		problems.AddRange(urls
			.Select(url => url.Groups["target"].Value.Trim())
			.Where(target => !AllowedUrl(target))
			.Select(target => $"url({target}) loads from elsewhere. Use a path on this site (starting with /) or a data:image URL."));

		if (!Balanced(text))
		{
			problems.Add("Braces do not balance: every { needs its }.");
		}

		// A colour token the contrast report cannot read (another selector, or a colour that is not #rrggbb) would
		// paint the page with one colour while the report, the derived shades and MudBlazor's palette use another.
		var declared = Unescape(bare);
		var readable = Readable(declared);
		var unreadable = TokenDeclarationPattern().Matches(declared)
			.Select(m => m.Groups["name"].Value)
			.Where(ThemeTokens.Editable.Contains)
			.GroupBy(name => name)
			.Where(g => g.Count() > readable.GetValueOrDefault(g.Key))
			.Select(g => $"--{g.Key}")
			.ToList();
		if (unreadable.Count > 0)
		{
			problems.Add($"Set {string.Join(", ", unreadable)} only in a plain :root rule, as a #rrggbb colour, so the contrast report can check it.");
		}

		return problems;
	}

	/// <summary>
	/// The colour tokens (<see cref="ThemeTokens.Editable"/>) that <paramref name="css"/> sets to a <c>#rrggbb</c> or
	/// <c>#rgb</c> colour in a <c>:root</c> rule, normalised to <c>#rrggbb</c>. The last one set wins.
	/// </summary>
	public static IReadOnlyDictionary<string, string> ColorOverrides(string? css)
	{
		var found = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(css) || !TryStripComments(css, out var bare))
		{
			return found;
		}

		foreach (var (name, value) in RootColors(Unescape(bare)))
		{
			found[name] = value;
		}

		return found;
	}

	/// <summary>Each colour token set to a <c>#rrggbb</c> or <c>#rgb</c> colour in a <c>:root</c> rule, in order.</summary>
	private static IEnumerable<(string Name, string Hex)> RootColors(string bare)
	{
		foreach (Match root in RootRulePattern().Matches(bare))
		{
			foreach (Match declaration in ColorDeclarationPattern().Matches(root.Groups["body"].Value))
			{
				var name = declaration.Groups["name"].Value;
				if (ThemeTokens.Editable.Contains(name) && ThemeColor.TryParse(declaration.Groups["value"].Value, out var color))
				{
					yield return (name, color.Hex);
				}
			}
		}
	}

	/// <summary>How many times each colour token is set where <see cref="ColorOverrides"/> reads it.</summary>
	private static Dictionary<string, int> Readable(string bare)
		=> RootColors(bare).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

	/// <summary><paramref name="tokens"/> with the colour tokens <paramref name="css"/> sets laid over them.</summary>
	public static IReadOnlyDictionary<string, string> WithColorOverrides(IReadOnlyDictionary<string, string> tokens, string? css)
	{
		var overrides = ColorOverrides(css);
		if (overrides.Count == 0)
		{
			return tokens;
		}

		var merged = new Dictionary<string, string>(tokens, StringComparer.Ordinal);
		foreach (var (name, value) in overrides)
		{
			merged[name] = value;
		}

		return merged;
	}

	/// <summary>
	/// A stylesheet to start from, which changes nothing until it is edited: what goes where, every variable the portal
	/// paints with at <paramref name="theme"/>'s value (commented out), and an empty rule for each of
	/// <see cref="Hooks"/> with what it reaches.
	/// </summary>
	public static string Starter(ResolvedTheme theme)
	{
		var css = new StringBuilder();
		css.Append(CultureInfo.InvariantCulture, $"/* {theme.Name.Replace("*/", "* /", StringComparison.Ordinal)}: the theme's own stylesheet.\n");
		css.Append("""

			   This loads after the theme's colours and style choices, so anything set here wins over them.

			   Where things belong:
			   1. Variables (:root, below). One change here reaches every page. Start here.
			      Each is listed with this theme's value, commented out: remove the comment marks
			      around a line to set it. A colour set here replaces the colour picker's, and the
			      contrast report checks it. A character's own accent still replaces --accent.
			   2. The shell: page background, rail, sidebars, top bar.
			   3. Cards and page headers.
			   4. Content: wiki pages and help.
			   5. Controls: buttons, fields, chips, tables.

			   Components keep their own scoped rules, so their classes below are doubled
			   (.kit-card.kit-card) to carry the same weight. Delete any rule you leave empty.

			   Not allowed, and refused on save: @import, url()s to other sites (use a path
			   on this site starting with /, or a data:image URL), HTML, and script. */


			""");

		css.Append("/* 1. Variables */\n:root {\n");
		foreach (var (group, properties) in Variables)
		{
			css.Append(CultureInfo.InvariantCulture, $"\t/* {group} */\n");
			foreach (var (name, what) in properties)
			{
				var value = theme.Tokens.TryGetValue(name, out var set) ? set : SheetDefaults.GetValueOrDefault(name);
				css.Append(CultureInfo.InvariantCulture, $"\t/* {what} */\n");
				// Drawings (textures, frames) are long data URLs: name the property and leave its value to the style choice.
				css.Append(value is null || value.Length > 120 || value.Contains("*/", StringComparison.Ordinal)
					? $"\t/* --{name}: (the style choice's); */\n"
					: $"\t/* --{name}: {value}; */\n");
			}
		}

		css.Append("}\n");

		var number = 2;
		foreach (var section in Hooks.GroupBy(h => h.Section switch { "Page headers" => "Cards", var s => s }))
		{
			var title = section.Key == "Cards" ? "Cards and page headers" : section.Key;
			css.Append(CultureInfo.InvariantCulture, $"\n/* {number++}. {title} */\n");
			foreach (var hook in section)
			{
				css.Append(CultureInfo.InvariantCulture, $"/* {hook.What} */\n{hook.Selector} {{\n}}\n");
			}
		}

		return css.ToString();
	}

	private static readonly (Regex Pattern, string What)[] Forbidden =
	[
		(new Regex(@"expression\s*\(", RegexOptions.CultureInvariant), "expression()"),
		(new Regex(@"(java|vb)script\s*:", RegexOptions.CultureInvariant), "A script: URL"),
		(new Regex(@"-moz-binding", RegexOptions.CultureInvariant), "-moz-binding"),
		(new Regex(@"(^|[^-a-z])behavior\s*:", RegexOptions.CultureInvariant), "behavior:"),
		(new Regex(@"image-set\s*\(", RegexOptions.CultureInvariant), "image-set() (use url())"),
		(new Regex(@"(^|[^-a-z])src\s*\(", RegexOptions.CultureInvariant), "src() (use url())"),
	];

	private static bool AllowedUrl(string target)
	{
		var unquoted = target.Trim('"', '\'').Trim();
		if (unquoted.StartsWith('#'))
		{
			return true;
		}

		if (unquoted.StartsWith('/') && !unquoted.StartsWith("//", StringComparison.Ordinal))
		{
			return true;
		}

		return DataImageTypes.Any(type => unquoted.StartsWith($"data:image/{type};", StringComparison.Ordinal)
			|| unquoted.StartsWith($"data:image/{type},", StringComparison.Ordinal));
	}

	/// <summary>The text with its comments taken out; false when a comment is never closed.</summary>
	private static bool TryStripComments(string css, out string bare)
	{
		var text = new StringBuilder(css.Length);
		var i = 0;
		char? quote = null;
		while (i < css.Length)
		{
			var c = css[i];
			if (quote is { } q)
			{
				text.Append(c);
				if (c == '\\' && i + 1 < css.Length)
				{
					text.Append(css[i + 1]);
					i += 2;
					continue;
				}

				if (c == q)
				{
					quote = null;
				}

				i++;
			}
			else if (c is '"' or '\'')
			{
				quote = c;
				text.Append(c);
				i++;
			}
			else if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
			{
				var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
				if (end < 0)
				{
					bare = string.Empty;
					return false;
				}

				// A comment separates tokens the way a space does.
				text.Append(' ');
				i = end + 2;
			}
			else
			{
				text.Append(c);
				i++;
			}
		}

		bare = text.ToString();
		return true;
	}

	/// <summary>CSS escapes read as the characters they stand for: <c>\75 rl(</c> is <c>url(</c>.</summary>
	private static string Unescape(string css)
		=> EscapePattern().Replace(css, m =>
		{
			if (m.Groups["hex"].Success)
			{
				var code = int.Parse(m.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
				return code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "\uFFFD";
			}

			return m.Groups["char"].Value is "\n" ? string.Empty : m.Groups["char"].Value;
		});

	/// <summary>Whether every <c>{</c> outside a string has its <c>}</c>, and none closes before it opens.</summary>
	private static bool Balanced(string css)
	{
		var depth = 0;
		char? quote = null;
		for (var i = 0; i < css.Length; i++)
		{
			var c = css[i];
			if (quote is { } q)
			{
				if (c == '\\')
				{
					i++;
				}
				else if (c == q)
				{
					quote = null;
				}
			}
			else if (c is '"' or '\'')
			{
				quote = c;
			}
			else if (c == '{')
			{
				depth++;
			}
			else if (c == '}' && --depth < 0)
			{
				return false;
			}
		}

		return depth == 0 && quote is null;
	}

	[GeneratedRegex(@"\\(?:(?<hex>[0-9a-fA-F]{1,6})(?:\r\n|[ \t\r\n\f])?|(?<char>[\s\S]))")]
	private static partial Regex EscapePattern();

	[GeneratedRegex(@"\burl\(\s*(?<target>""[^""]*""|'[^']*'|[^)]*)\s*\)")]
	private static partial Regex UrlPattern();

	[GeneratedRegex(@"(?<![\w-]):root\s*\{(?<body>[^{}]*)\}", RegexOptions.IgnoreCase)]
	private static partial Regex RootRulePattern();

	[GeneratedRegex(@"--(?<name>[a-z0-9-]+)\s*:\s*(?<value>#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3})\s*(?:!important\s*)?(?:;|$)")]
	private static partial Regex ColorDeclarationPattern();

	[GeneratedRegex(@"--(?<name>[a-z0-9-]+)\s*:")]
	private static partial Regex TokenDeclarationPattern();
}
