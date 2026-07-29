using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Guards the CJK font switch. Chinese, Japanese and Korean need a monospace face whose Latin glyphs
/// are exactly half the width of its CJK ones, because the terminal measures a character grid and
/// reports it to the server over NAWS. That is arranged by overriding a single custom property per
/// language — which holds only while every mono surface actually reads the property.
/// </summary>
public class MonoFontStackTests
{
	private static string Css() => File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "custom.css"));

	private static IEnumerable<string> ComponentSources() =>
		Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "client", "razor"), "*.*", SearchOption.AllDirectories)
			.Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".css", StringComparison.Ordinal));

	[Test]
	public async Task TheMonoStackIsDefinedOnceAndSwappedForCjkLocales()
	{
		var css = Css();

		await Assert.That(css).Contains("--font-mono:")
			.Because("the whole switch works by overriding one custom property");

		// One rule, all three scripts: Japanese and Korean render the same Latin-to-CJK ratio problem,
		// and shipping the arm now means adding those locales is a resx job rather than a CSS job.
		foreach (var lang in new[] { "zh", "ja", "ko" })
		{
			await Assert.That(css).Contains($":lang({lang})")
				.Because($"{lang} needs the CJK mono face, not per-glyph fallback from a Latin one");
		}

		var cjkRule = Regex.Match(css, @":root:lang\(zh\)[^{]*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(cjkRule.Success).IsTrue().Because("the zh arm should be a :root rule so it wins on specificity");
		await Assert.That(cjkRule.Groups["body"].Value).Contains("--font-mono:")
			.Because("the CJK arm has to replace the stack, not merely append a fallback: mixing two "
				+ "faces means the Latin advance width is no longer half the CJK one");
	}

	[Test]
	public async Task TheCjkFaceIsSelfHostedRatherThanFetchedFromAThirdParty()
	{
		var css = Css();
		var faces = Regex.Matches(css, @"@font-face\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline)
			.Select(m => m.Groups["body"].Value)
			.ToList();

		await Assert.That(faces).IsNotEmpty();

		var sources = faces.SelectMany(b => Regex.Matches(b, @"url\((?<url>[^)]*)\)").Select(m => m.Groups["url"].Value.Trim('"', '\''))).ToList();
		await Assert.That(sources).IsNotEmpty();

		foreach (var url in sources)
		{
			// A CDN would put a font on the critical path for a portal that may run on a closed network,
			// and would leak every reader's IP to a third party.
			await Assert.That(url.StartsWith("/fonts/", StringComparison.Ordinal)).IsTrue()
				.Because($"'{url}' should be served from the app's own wwwroot");
		}
	}

	[Test]
	public async Task TheShellFetchesNothingFromAThirdParty()
	{
		// Every font and script is served from wwwroot, so the portal renders identically on a LAN
		// with no route to the internet — and no third party is handed the IP of everyone who opens
		// the game's front page. A stylesheet from a CDN cannot carry an SRI hash either, so it is
		// also the one asset class that could change under us without any local change.
		var html = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "index.html"));

		// Matched loosely on purpose. HTML permits double quotes, single quotes and no quotes at all,
		// and attribute names are case-insensitive, so a pattern that only understands lowercase
		// double-quoted attributes would wave through the exact thing this test exists to stop.
		// Protocol-relative URLs count too: //cdn.example.com is every bit as third-party.
		var external = Regex.Matches(html,
				"""(?:src|href)\s*=\s*(?:"(?<url>[^"]*)"|'(?<url>[^']*)'|(?<url>[^\s>]+))""",
				RegexOptions.IgnoreCase)
			.Select(m => m.Groups["url"].Value.Trim())
			.Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
				|| u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
				|| u.StartsWith("//", StringComparison.Ordinal))
			.ToList();

		await Assert.That(external).IsEmpty()
			.Because("index.html should reference only same-origin assets");
	}

	[Test]
	public async Task NoComponentPinsAMonoFontDirectly()
	{
		// The failure this catches is silent: a hardcoded family renders perfectly in English and only
		// misaligns once someone selects Chinese, on that one element. Components have no legitimate
		// reason to name a face — @font-face lives in custom.css, which this sweep deliberately skips.
		var offenders = new List<string>();

		foreach (var file in ComponentSources())
		{
			var text = File.ReadAllText(file);
			foreach (Match m in Regex.Matches(text, @"font-family:\s*(?<value>[^;""}]*)"))
			{
				var value = m.Groups["value"].Value.Trim();
				if (value.StartsWith("var(", StringComparison.Ordinal) || value == "inherit")
					continue;

				offenders.Add($"{Path.GetFileName(file)}: font-family: {value}");
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("every mono surface must read var(--font-mono) so the CJK locale switch reaches it");
	}
}
