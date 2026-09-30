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

	[Test]
	public async Task TextFaintMeetsAa()
	{
		await Assert.That(Tokens()).Contains("--text-faint: #7d8790")
			.Because("#5f6870 is 3.38:1 on --bg; the small labels it is used on need 4.5:1");
	}

	[Test]
	public async Task OldTextFaintValueIsGone()
	{
		await Assert.That(Tokens()).DoesNotContain("#5f6870");
	}
}
