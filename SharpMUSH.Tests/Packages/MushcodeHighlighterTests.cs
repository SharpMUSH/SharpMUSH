using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>Phase 8: MUSHcode highlighting and dangerous-pattern scanning.</summary>
public class MushcodeHighlighterTests
{
	[Test]
	public async Task Highlights_CommandPattern_Functions_Subs_Dbrefs_Refs()
	{
		var html = MushcodeHighlighter.ToHtml(
			"$+who *:@pemit %#=[u({{ww_fn}}/FMT,%0)] see #123:456");

		await Assert.That(html).Contains("<span class=\"mush-cmdpattern\">$+who *:</span>");
		await Assert.That(html).Contains("<span class=\"mush-atcmd\">@pemit</span>");
		await Assert.That(html).Contains("<span class=\"mush-sub\">%#</span>");
		await Assert.That(html).Contains("<span class=\"mush-sub\">%0</span>");
		await Assert.That(html).Contains("<span class=\"mush-fn\">u</span>(");
		await Assert.That(html).Contains("<span class=\"mush-ref\">{{ww_fn}}</span>");
		await Assert.That(html).Contains("<span class=\"mush-dbref\">#123:456</span>");
	}

	[Test]
	public async Task Output_IsHtmlEncoded()
	{
		var html = MushcodeHighlighter.ToHtml("think <b>not html</b> & stuff");
		await Assert.That(html).DoesNotContain("<b>");
		await Assert.That(html).Contains("&lt;b&gt;");
		await Assert.That(html).Contains("&amp;");
	}

	[Test]
	public async Task DangerousPatterns_FlaggedAndWrapped()
	{
		var value = "$+admin *:@force %0=look; @pemit %#=done";
		var patterns = MushcodeHighlighter.FindDangerousPatterns(value);
		await Assert.That(patterns).Contains("@force");

		var html = MushcodeHighlighter.ToHtml(value);
		await Assert.That(html).Contains("mush-danger");

		await Assert.That(MushcodeHighlighter.FindDangerousPatterns("pemit( *grep, hi)")).Contains("pemit(*");
		await Assert.That(MushcodeHighlighter.FindDangerousPatterns("plain @pemit text").Count).IsEqualTo(0);
	}

	[Test]
	public async Task NamedRegisters_AndPercentQ()
	{
		var html = MushcodeHighlighter.ToHtml("setq(0,%q<myname>) %q0 %qa");
		await Assert.That(html).Contains("<span class=\"mush-sub\">%q&lt;myname&gt;</span>");
		await Assert.That(html).Contains("<span class=\"mush-sub\">%q0</span>");
		await Assert.That(html).Contains("<span class=\"mush-sub\">%qa</span>");
	}
}
