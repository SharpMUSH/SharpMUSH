using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Pins the D1 token set (docs/design/d1/README.md §2) in tokens.css.</summary>
public class DesignTokensTests
{
	private static string Tokens() => File.ReadAllText(Path.Join(ClientSource.CssRoot, "tokens.css"));

	private static readonly string[] D1Tokens =
	[
		"--rail-bg", "--text-on-image", "--link-missing", "--warn", "--warn-tint", "--unread-alert",
		"--ooc-band-bg", "--ooc-band-border", "--ooc-icon-bg", "--ooc-icon-fg",
		"--rail-w", "--side-w", "--side-strip-w", "--aside-w", "--main-pad",
		"--radius-card", "--radius-row", "--radius-tile", "--radius-portrait",
		"--glass-bg", "--glass-pill-bg", "--glass-filter", "--glass-edge", "--on-image-shadow",
		"--scrim", "--scrim-open",
		"--blur-mask-soft", "--blur-mask-mid", "--blur-mask-deep",
		"--blur-mask-soft-open", "--blur-mask-mid-open", "--blur-mask-deep-open",
		"--mention-offset", "--mention-alpha",
		"--switch-knob-off", "--topbar-h",
	];

	[Test]
	public async Task EveryD1TokenIsDeclaredOnRoot()
	{
		var css = Tokens();
		var missing = D1Tokens
			.Where(t => !Regex.IsMatch(css, $@"^\s*{Regex.Escape(t)}\s*:", RegexOptions.Multiline))
			.ToList();
		await Assert.That(missing).IsEmpty()
			.Because("README §2 lists these as the additions every kit piece reads");
	}

	/// <summary>
	/// README §2: "the §4.3 scrim keeps light text above 4.5:1 on bright art". Banner art is whatever a game
	/// uploads, so the worst case is a white image: white text over the scrim composited on white must reach
	/// 4.5:1 through the band the text occupies — the lower half of a closed banner, and up to 72% of an open
	/// one, where the description sits.
	/// </summary>
	[Test]
	[Arguments("--scrim", 50)]
	[Arguments("--scrim-open", 72)]
	public async Task TheScrimKeepsLightTextReadable_OnWhiteArt(string token, int textBandPercent)
	{
		var stops = ScrimStops(token);
		for (var at = 0; at <= textBandPercent; at++)
		{
			var alpha = AlphaAt(stops, at);
			// rgba(10, 11, 13, alpha) over white, as sRGB channels 0..255.
			double Channel(double c) => alpha * c + (1 - alpha) * 255;
			var background = Luminance(Channel(10), Channel(11), Channel(13));
			var ratio = 1.05 / (background + 0.05);
			await Assert.That(ratio).IsGreaterThanOrEqualTo(4.5)
				.Because($"{token} at {at}% from the bottom has alpha {alpha:0.00}");
		}
	}

	private static List<(double Alpha, double At)> ScrimStops(string token)
	{
		var line = Regex.Match(Tokens(), $@"^\s*{Regex.Escape(token)}\s*:(?<value>[^;]+);", RegexOptions.Multiline).Groups["value"].Value;
		return Regex.Matches(line, @"rgba\(\s*10,\s*11,\s*13,\s*(?<a>[\d.]+)\)\s*(?<p>[\d.]+)%")
			.Select(m => (double.Parse(m.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture),
				double.Parse(m.Groups["p"].Value, System.Globalization.CultureInfo.InvariantCulture)))
			.ToList();
	}

	private static double AlphaAt(List<(double Alpha, double At)> stops, double at)
	{
		for (var i = 1; i < stops.Count; i++)
		{
			if (at <= stops[i].At)
			{
				var (a0, p0) = stops[i - 1];
				var (a1, p1) = stops[i];
				return a0 + (a1 - a0) * (at - p0) / (p1 - p0);
			}
		}
		return stops[^1].Alpha;
	}

	private static double Luminance(double r, double g, double b)
	{
		static double Linear(double c)
		{
			c /= 255;
			return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
		}
		return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
	}

	[Test]
	public async Task TextFaintMeetsAa()
	{
		await Assert.That(Tokens()).Contains("--text-faint: #7d8790")
			.Because("#5f6870 is 3.38:1 on --bg; the small labels it is used on need 4.5:1");
	}

	[Test]
	public async Task CoarsePointerRuleCoversTheKitControls()
	{
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));
		// The documentation block near the top of shell.css quotes the query in prose; the rule itself starts a line.
		var rule = Regex.Match(shell, @"^@media \(pointer: coarse\)", RegexOptions.Multiline);
		await Assert.That(rule.Success).IsTrue();
		var start = rule.Index;
		var open = shell.IndexOf('{', start);
		var depth = 0;
		var end = open;
		for (; end < shell.Length; end++)
		{
			if (shell[end] == '{') depth++;
			else if (shell[end] == '}' && --depth == 0) break;
		}
		var block = shell[start..end];
		foreach (var selector in new[] { ".kit-row", ".kit-capsule", ".kit-chip", ".kit-capsule--icon", ".kit-portrait", ".kit-tile" })
		{
			await Assert.That(block).Contains(selector)
				.Because("README §9: targets are 44px under a coarse pointer, and only the shell may say so");
		}
	}

	[Test]
	public async Task TheTopbarHeightIsATokenTheConfigStickyBoxSubtracts()
	{
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));
		await Assert.That(shell).Contains("height: var(--topbar-h)")
			.Because("a sticky box inside the scroll container must know how tall the topbar above it is");
		var layout = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Layout", "ConfigLayout.razor.css"));
		await Assert.That(layout).Contains("calc(100dvh - var(--topbar-h)")
			.Because("a 100dvh sticky sidebar under a 60px topbar hides its bottom 60px — the pinned Maintenance rows");
	}

	[Test]
	public async Task OldTextFaintValueIsGone()
	{
		await Assert.That(Tokens()).DoesNotContain("#5f6870");
	}
}
