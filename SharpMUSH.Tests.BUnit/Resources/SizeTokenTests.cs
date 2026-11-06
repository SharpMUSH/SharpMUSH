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
			foreach (Match style in StyleAttribute().Matches(File.ReadAllText(file)))
			{
				Check(Path.GetFileName(file), style.Groups["css"].Value, offenders);
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a font size, padding or gap belongs on the scale in tokens.css; use a --text-* or --space-* step");
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

	[GeneratedRegex(@"(?<![\w-])[Ss]tyle=""(?<css>[^""]*)""")]
	private static partial Regex StyleAttribute();

	[GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
	private static partial Regex Comment();
}
