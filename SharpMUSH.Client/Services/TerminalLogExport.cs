using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A stretch of the play terminal's output (the saved log, <see cref="TerminalLog"/>, or what the terminal holds) written as a file to save: plain text, or one
/// HTML page that keeps the game's colours and layout and needs nothing else to open.
/// </summary>
/// <remarks>
/// <para>The lines are the kept ones, so what acts when rendered (sounds, media, a clear screen) is already taken out
/// (<see cref="TerminalScrollback"/>). The HTML page also refuses scripts and every request but pictures, through its
/// own content security policy, so a saved log opened later does nothing but show.</para>
/// <para>Times are the browser's, as the terminal shows them.</para>
/// </remarks>
public static partial class TerminalLogExport
{
	/// <summary>
	/// The theme's custom properties an HTML export copies, as the page has them when the player exports, and the terminal
	/// pane's own background and text colour (<c>--log-bg</c>, <c>--log-fg</c>), which the script reads from the pane.
	/// </summary>
	public static readonly string[] ThemeProperties =
	[
		"--surface", "--text", "--text-dim", "--text-faint", "--accent", "--special", "--tone-tertiary", "--highlight",
		"--success", "--warn", "--danger", "--info", "--hue-red", "--hue-orange", "--hue-yellow", "--hue-green",
		"--hue-cyan", "--hue-blue", "--hue-purple", "--hue-pink", "--font-mono", "--log-bg", "--log-fg"
	];

	/// <summary>How the exported lines look: the markup stylesheet, and the theme's values of <see cref="ThemeProperties"/>.</summary>
	/// <remarks>
	/// <see cref="Vars"/> also holds the markup's own properties (<c>--ms-*</c>: <c>ansi()</c> colours, layout borders and
	/// titles) as the theme sets them for the terminal. <see cref="Origin"/> is the portal's, for pictures sent by path.
	/// </remarks>
	public sealed record Style(
		[property: JsonPropertyName("css")] string? Css,
		[property: JsonPropertyName("vars")] IReadOnlyDictionary<string, string>? Vars,
		[property: JsonPropertyName("origin")] string? Origin = null)
	{
		/// <summary>None of it: the page falls back to its own plain colours.</summary>
		public static readonly Style None = new(null, null);
	}

	/// <summary>The file's name: the character, the range's start and end, and the format's extension.</summary>
	public static string FileName(string character, DateTime from, DateTime to, TerminalLogFormat format)
	{
		var name = UnsafeInFileName().Replace(character, string.Empty).Trim();
		var stem = $"{(name.Length == 0 ? "log" : name)} {Stamp(from)} to {Stamp(to)}";
		return format switch
		{
			TerminalLogFormat.Html => stem + ".html",
			_ => stem + ".txt",
		};

		static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
	}

	/// <summary>The MIME type the browser saves the file as.</summary>
	public static string MimeType(TerminalLogFormat format) => format switch
	{
		TerminalLogFormat.Html => "text/html",
		_ => "text/plain",
	};

	/// <summary>
	/// The lines as plain text, under a heading naming <paramref name="character"/> and the range. With
	/// <paramref name="times"/> each line starts with its time, and the lines a line breaks into line up under its text.
	/// </summary>
	public static string Text(IReadOnlyList<TerminalLine> lines, string character, DateTime from, DateTime to, bool times)
	{
		var text = new StringBuilder();
		text.Append(Heading(character, from, to)).Append('\n').Append('\n');
		foreach (var line in lines)
		{
			// A laid-out line pads its rows to the terminal's width; a file has no width to fill.
			var body = string.Join('\n', line.Text.ReplaceLineEndings("\n").Split('\n').Select(row => row.TrimEnd()));
			if (!times)
			{
				text.Append(body).Append('\n');
				continue;
			}
			var stamp = $"[{Time(line.Timestamp)}] ";
			text.Append(stamp).Append(body.Replace("\n", "\n" + new string(' ', stamp.Length))).Append('\n');
		}
		return text.ToString();
	}

	/// <summary>
	/// The lines as one HTML page that opens on its own: the markup stylesheet and the theme's colours inline, each line
	/// as the terminal renders it, with its time beside it when <paramref name="times"/>.
	/// </summary>
	public static string Html(IReadOnlyList<TerminalLine> lines, string character, DateTime from, DateTime to, bool times, Style style)
	{
		var heading = WebUtility.HtmlEncode(Heading(character, from, to));
		var origin = Origin(style.Origin);
		var html = new StringBuilder();
		html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
			.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src https: data:")
			.Append(origin is null ? string.Empty : " " + origin).Append("\">\n")
			.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
			.Append("<title>").Append(heading).Append("</title>\n<style>\n")
			.Append(Variables(style.Vars))
			.Append(Contained(style.Css ?? string.Empty)).Append('\n')
			.Append(PageCss)
			.Append("</style>\n</head>\n<body>\n<h1>").Append(heading).Append("</h1>\n<main>\n");
		foreach (var line in lines)
		{
			html.Append("<div class=\"line\">");
			if (times)
			{
				html.Append("<time datetime=\"")
					.Append(new DateTimeOffset(line.Timestamp).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
					.Append("\">").Append(Time(line.Timestamp)).Append("</time>");
			}
			html.Append("<span class=\"text\">").Append(Pictures(line.Html, origin)).Append("</span></div>\n");
		}
		html.Append("</main>\n</body>\n</html>\n");
		return html.ToString();
	}

	/// <summary>"Ada, 2026-11-25 17:00 to 2026-11-25 18:00".</summary>
	private static string Heading(string character, DateTime from, DateTime to) =>
		$"{character}, {from.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} to {to.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

	private static string Time(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary>
	/// The theme's values as <c>:root</c> properties: <see cref="ThemeProperties"/> and the markup's <c>--ms-*</c> ones,
	/// leaving out any name or value that could end the rule or the style element.
	/// </summary>
	private static string Variables(IReadOnlyDictionary<string, string>? vars)
	{
		var root = new StringBuilder(":root {\n");
		var names = ThemeProperties.Concat((vars?.Keys ?? []).Where(n => n.StartsWith("--ms-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
		var safe = names.Distinct()
			.Where(name => SafeName().IsMatch(name))
			.Select(name => (Name: name, Value: vars?.GetValueOrDefault(name)))
			.Where(pair => pair.Value is { Length: > 0 } value && SafeValue().IsMatch(value));
		foreach (var (name, value) in safe)
			root.Append("  ").Append(name).Append(": ").Append(value).Append(";\n");
		return root.Append("}\n").ToString();
	}

	/// <summary>The portal's origin, when it is one (<c>http(s)://host[:port]</c>): pictures sent by path load from it.</summary>
	private static string? Origin(string? origin) =>
		Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
			? uri.GetLeftPart(UriPartial.Authority)
			: null;

	/// <summary>A picture the game sent by path loads from the portal, as it did on screen; a saved file has no portal of its own.</summary>
	private static string Pictures(string html, string? origin) =>
		origin is null ? html : PictureByPath().Replace(html, m => $"{m.Groups[1].Value}{origin}/{m.Groups[2].Value.TrimStart('/')}\"");

	/// <summary>A stylesheet that cannot close the style element it sits in.</summary>
	private static string Contained(string css) => css.Replace("</", "<\\/", StringComparison.Ordinal);

	/// <summary>
	/// The page's own rules: the terminal's pane, then the portal's rules for game markup that the package's stylesheet
	/// leaves to its host (shell.css), with a plain fallback for each colour a theme did not give.
	/// </summary>
	private const string PageCss = """
		body { margin: 0; padding: 24px; background: var(--log-bg, var(--surface, #111)); color: var(--log-fg, var(--text, #ddd));
		  font-family: var(--font-mono, ui-monospace, monospace); font-variant-ligatures: none; }
		h1 { margin: 0 0 16px; font-size: 1rem; font-weight: 600; color: var(--text-dim, #aaa); }
		.line { display: flex; gap: 12px; line-height: 1.6; }
		.line time { flex: none; color: var(--text-faint, #888); white-space: nowrap; }
		.line .text { flex: 1; min-width: 0; white-space: pre-wrap; overflow-wrap: break-word; }
		.line img { max-width: 100%; height: auto; }
		.ms-underline.ms-strike { text-decoration: underline line-through; }
		pre.ms-preformatted { display: inline; margin: 0; font: inherit; white-space: pre-wrap; }
		pre.ms-preformatted:has(> [style*="background-color"]) { display: inline-block; vertical-align: text-top; line-height: normal; }
		.ms-layout { display: inline-block; width: 100%; vertical-align: top; }
		.ms-rule:empty, .ms-divider:empty { gap: 0; min-height: 1lh; }
		.ms-table { word-break: normal; overflow-wrap: break-word; }
		.ms-cmd-link { color: var(--accent, #6af); }
		.tone-foreground { color: var(--text); }
		.tone-strong { color: var(--text); font-weight: 700; }
		.tone-primary, .tone-link { color: var(--accent); }
		.tone-secondary { color: var(--special); }
		.tone-tertiary { color: var(--tone-tertiary); }
		.tone-muted { color: var(--text-dim); }
		.tone-subtle { color: var(--text-faint); }
		.tone-highlight { background-color: var(--highlight); color: var(--text); }
		.tone-success { color: var(--success); }
		.tone-warning { color: var(--warn); }
		.tone-error { color: var(--danger); }
		.tone-info { color: var(--info); }
		.tone-red { color: var(--hue-red); }
		.tone-orange { color: var(--hue-orange); }
		.tone-yellow { color: var(--hue-yellow); }
		.tone-green { color: var(--hue-green); }
		.tone-cyan { color: var(--hue-cyan); }
		.tone-blue { color: var(--hue-blue); }
		.tone-purple { color: var(--hue-purple); }
		.tone-pink { color: var(--hue-pink); }
		:is(.ms-shaded, .ms-gauge-bar) {
		  --ms-tone-foreground: var(--text); --ms-tone-primary: var(--accent); --ms-tone-secondary: var(--special);
		  --ms-tone-tertiary: var(--tone-tertiary); --ms-tone-muted: var(--text-dim); --ms-tone-subtle: var(--text-faint);
		  --ms-tone-link: var(--accent); --ms-tone-highlight: var(--highlight); --ms-tone-success: var(--success);
		  --ms-tone-warning: var(--warn); --ms-tone-error: var(--danger); --ms-tone-info: var(--info);
		  --ms-tone-red: var(--hue-red); --ms-tone-orange: var(--hue-orange); --ms-tone-yellow: var(--hue-yellow);
		  --ms-tone-green: var(--hue-green); --ms-tone-cyan: var(--hue-cyan); --ms-tone-blue: var(--hue-blue);
		  --ms-tone-purple: var(--hue-purple); --ms-tone-pink: var(--hue-pink);
		}

		""";

	[GeneratedRegex(@"[\\/:*?""<>|\x00-\x1f]")]
	private static partial Regex UnsafeInFileName();

	[GeneratedRegex(@"^--[a-z0-9-]+$")]
	private static partial Regex SafeName();

	// The src of an <img> that is a path rather than an address.
	[GeneratedRegex(@"(<img\b[^>]*?\bsrc="")(?!https?:|data:|//)([^""]*)""", RegexOptions.IgnoreCase)]
	private static partial Regex PictureByPath();

	// Colours, lengths and font lists: no braces, semicolons or angle brackets.
	[GeneratedRegex(@"^[^{}<>;\\]+$")]
	private static partial Regex SafeValue();
}

/// <summary>What a log export is written as.</summary>
public enum TerminalLogFormat
{
	Text,
	Html,
}
