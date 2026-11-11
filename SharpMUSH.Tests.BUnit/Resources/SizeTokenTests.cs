using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Every font size, padding and gap in the portal is a step of the type and space scales in <c>tokens.css</c>
/// (<c>--text-*</c>, <c>--space-*</c>), so every page sets text and room the same way. A size relative to its
/// surroundings (em, %, a viewport unit) is allowed: it follows the scale of the text around it.
/// </summary>
public partial class SizeTokenTests
{
	private static IEnumerable<string> PortalCss() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.css", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(ClientSource.CssRoot, "*.css", SearchOption.AllDirectories));

	private static IEnumerable<string> PortalMarkup() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories);

	[Test]
	public async Task NoStylesheetSizesTextOrSpaceOfItsOwn()
	{
		var offenders = new List<string>();
		foreach (var file in PortalCss())
		{
			Check(Path.GetFileName(file), Comment().Replace(File.ReadAllText(file), ""), offenders);
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a font size, padding or gap belongs on the scale in tokens.css; use a --text-* or --space-* step");
	}

	[Test]
	public async Task NoInlineStyleSizesTextOrSpaceOfItsOwn()
	{
		var offenders = new List<string>();
		foreach (var file in PortalMarkup())
		{
			foreach (var css in InlineStyles(File.ReadAllText(file)))
			{
				Check(Path.GetFileName(file), css, offenders);
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a font size, padding or gap belongs on the scale in tokens.css; use a --text-* or --space-* step");
	}

	/// <summary>
	/// The page the browser shows before the portal starts (the boot skeleton, the insecure-origin notice) is held to
	/// the same scale, in its own stylesheet and in its style attributes.
	/// </summary>
	[Test]
	public async Task TheHostPageSizesNoTextOrSpaceOfItsOwn()
	{
		var html = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "..", "index.html"));
		var offenders = new List<string>();
		foreach (Match sheet in StyleElement().Matches(html))
		{
			Check("index.html", Comment().Replace(sheet.Groups["css"].Value, ""), offenders);
		}

		foreach (var css in InlineStyles(html))
		{
			Check("index.html", css, offenders);
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a font size, padding or gap belongs on the scale in tokens.css; use a --text-* or --space-* step");
	}

	/// <summary>A style built in C# is read too: its string literals, with each interpolation hole left out.</summary>
	[Test]
	public async Task TheCheckReadsInterpolatedStyles()
	{
		var styles = InlineStyles("""
			<div style="@($"gap:0.25rem;background:{(alt ? "var(--a)" : "var(--b)")};padding:0.5rem")"></div>
			<MudIcon Style="@($"font-size:0.9375rem;color:{(on ? "var(--accent)" : "12px")};")" />
			<span style="@(wide ? "padding:var(--space-2)" : "padding:3px")"></span>
			<span style="@($"padding-left:{depth * 1.25}rem;")"></span>
			<b style="font-size: 9px"></b>
			""").ToList();

		var offenders = new List<string>();
		foreach (var css in styles)
		{
			Check("sample.razor", css, offenders);
		}

		// gap and padding on the first, font-size on the second, the ternary's raw branch, the plain attribute. The
		// hole's own value (12px) and the computed padding-left are not literals the markup wrote.
		await Assert.That(offenders).Count().IsEqualTo(5);
	}

	/// <summary>
	/// Each <c>style="…"</c> or <c>Style="…"</c> attribute's CSS. A plain attribute is its text; a Razor expression
	/// (<c>style="@($"gap:0.25rem;{x}")"</c>) is the text of its string literals, joined with <c>;</c>, each
	/// interpolation hole left out, since what a hole computes is not written in the markup.
	/// </summary>
	private static IEnumerable<string> InlineStyles(string markup)
	{
		foreach (Match style in StyleAttribute().Matches(markup))
		{
			if (!style.Groups["css"].Value.StartsWith("@(", StringComparison.Ordinal))
			{
				yield return style.Groups["css"].Value;
				continue;
			}

			var css = new System.Text.StringBuilder();
			var i = style.Groups["css"].Index + 2;
			for (var depth = 1; i < markup.Length && depth > 0;)
			{
				switch (markup[i])
				{
					case '"' or '$' when markup[i] == '"' || (i + 1 < markup.Length && markup[i + 1] == '"'):
						i = ReadString(markup, i, css);
						css.Append(';');
						continue;
					case '(':
						depth++;
						break;
					case ')':
						depth--;
						break;
				}

				i++;
			}

			yield return css.ToString();
		}
	}

	/// <summary>
	/// Reads the C# string literal at <paramref name="i"/> (<c>"…"</c> or <c>$"…"</c>) into <paramref name="text"/>,
	/// leaving out its holes, and returns the index after it.
	/// </summary>
	private static int ReadString(string source, int i, System.Text.StringBuilder text)
	{
		var interpolated = source[i] == '$';
		i += interpolated ? 2 : 1;
		while (i < source.Length && source[i] != '"')
		{
			switch (source[i])
			{
				case '\\' when i + 1 < source.Length:
					text.Append(source[i + 1]);
					i += 2;
					break;
				case '{' or '}' when interpolated && i + 1 < source.Length && source[i + 1] == source[i]:
					text.Append(source[i]);
					i += 2;
					break;
				case '{' when interpolated:
					i = SkipHole(source, i + 1);
					break;
				default:
					text.Append(source[i]);
					i++;
					break;
			}
		}

		return i + 1;
	}

	/// <summary>Returns the index after the interpolation hole that starts at <paramref name="i"/>.</summary>
	private static int SkipHole(string source, int i)
	{
		for (var depth = 1; i < source.Length && depth > 0;)
		{
			switch (source[i])
			{
				case '"' or '$' when source[i] == '"' || (i + 1 < source.Length && source[i + 1] == '"'):
					i = ReadString(source, i, new System.Text.StringBuilder());
					continue;
				case '{':
					depth++;
					break;
				case '}':
					depth--;
					break;
			}

			i++;
		}

		return i;
	}

	[Test]
	public async Task TheCheckCatchesRawSizes()
	{
		var offenders = new List<string>();
		Check("sample.css", """
			.a { font-size: 13px; }
			.b { padding: var(--space-3) 10px; }
			.c { gap: 0.5rem; }
			.d { font: 600 12px/1.4 var(--font-ui); }
			.e { padding-inline: calc(var(--space-3) + 1px); }
			.ok { font-size: var(--text-sm); padding: 0 var(--space-4) 0.5em; gap: 0; font: 600 var(--text-xs)/18px var(--font-mono); }
			.ok2 { --card-padding: 10px; padding: env(safe-area-inset-bottom, 0px); font-size: 0.85em; }
			""", offenders);

		await Assert.That(offenders).Count().IsEqualTo(5);
	}

	private static void Check(string file, string css, List<string> offenders)
	{
		foreach (Match m in Declaration().Matches(css))
		{
			var value = m.Groups["value"].Value;
			// In the font shorthand only the size counts; the line height after the slash is not on the scale.
			if (m.Groups["prop"].Value == "font")
			{
				value = LineHeight().Replace(value, "");
			}

			if (Literal().Match(Variable().Replace(value, "")) is { Success: true } literal)
			{
				offenders.Add($"{file}: {m.Value.Trim()} ({literal.Value})");
			}
		}
	}

	[GeneratedRegex(@"(?<![\w-])(?<prop>font-size|font|padding(?:-[a-z-]+)?|gap|row-gap|column-gap)\s*:\s*(?<value>[^;{}""]+)")]
	private static partial Regex Declaration();

	[GeneratedRegex(@"var\([^()]*\)")]
	private static partial Regex Variable();

	[GeneratedRegex(@"/\s*[\d.]+[a-z%]*")]
	private static partial Regex LineHeight();

	// An absolute length other than zero.
	[GeneratedRegex(@"(?<![\w.-])(?:0*[1-9]\d*(?:\.\d+)?|0*\.\d*[1-9]\d*)(?:px|rem|pt)\b")]
	private static partial Regex Literal();

	// A Razor expression's own quotes end the plain match early; InlineStyles reads such a value from its "@(".
	[GeneratedRegex(@"(?<![\w-])[Ss]tyle=""(?<css>[^""]*)""")]
	private static partial Regex StyleAttribute();

	[GeneratedRegex(@"<style[^>]*>(?<css>.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex StyleElement();

	[GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
	private static partial Regex Comment();
}
