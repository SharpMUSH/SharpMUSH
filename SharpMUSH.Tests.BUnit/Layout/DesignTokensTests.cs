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
	public async Task ThePageSidebar_IsAShellColumnThatScrollsOnItsOwn_AndIsGoneWhenEmpty()
	{
		// README §3: the page sidebar is a shell slot, 232px (62 collapsed), not a sticky box inside the
		// page that has to subtract the chrome above it from 100dvh.
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));
		var start = shell.IndexOf(".phosphor-pagebar {", StringComparison.Ordinal);
		var block = shell[start..shell.IndexOf('}', start)];
		await Assert.That(block).Contains("width: var(--side-w)");
		await Assert.That(block).Contains("overflow-y: auto");
		await Assert.That(shell).Contains(".phosphor-pagebar:not(:has(.kit-pagebar))");
		await Assert.That(shell).Contains(".phosphor-pagebar:has(.kit-pagebar--collapsed)");
	}

	[Test]
	public async Task WikiLinksUseTheAccent_RedlinksTheMissingColour_AndMentionsInherit()
	{
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));
		var links = shell[shell.IndexOf(".WikiContent .mud-card-content a {", StringComparison.Ordinal)..];
		await Assert.That(links[..links.IndexOf('}')]).Contains("var(--accent)")
			.Because("README §4.8: wiki links change from the MudBlazor secondary to the accent");
		var red = shell[shell.IndexOf(".WikiContent .mud-card-content a.wiki-redlink {", StringComparison.Ordinal)..];
		await Assert.That(red[..red.IndexOf('}')]).Contains("var(--link-missing)");
		await Assert.That(shell).Contains(".WikiContent .mud-card-content a.mention {");
		await Assert.That(shell).Contains(".WikiContent .mud-card-content a.wiki-redlink:not(.mention) {")
			.Because("a redlinked mention keeps the mention underline; the dashed border would be a second one");
	}

	[Test]
	public async Task OldTextFaintValueIsGone()
	{
		await Assert.That(Tokens()).DoesNotContain("#5f6870");
	}
}
