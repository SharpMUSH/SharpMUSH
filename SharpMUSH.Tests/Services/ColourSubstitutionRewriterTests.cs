using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="ColourSubstitutionRewriter"/> turns <c>%c</c>/<c>%x</c> colour substitutions into <c>ansi()</c>.
/// The expected rewrites are the project's colour battery, each of which was run on a RhostMUSH server
/// before and after the rewrite and printed the same styled text, except where a flag says why not.
/// </summary>
public class ColourSubstitutionRewriterTests
{
	private static readonly ColourRewriteOptions RhostExpression =
		new(ColourSubstitutionDialect.RhostMush, ColourRewriteMode.Expression);

	[Test]
	[Arguments("%crRED%cnplain", "ansi(r,RED)plain", "")]
	[Arguments("%crA[name(me)]B%cn", "ansi(r,A[name(me)]B)", "")]
	[Arguments("%ch[ifelse(1,%cc,%cr)]X%cn", "ansi(h[ifelse(1,c,r)],X)", "")]
	[Arguments("%ch[ifelse(0,%cc,%cr)]X%cn", "ansi(h[ifelse(0,c,r)],X)", "")]
	[Arguments("%crHello, world (hi) %cnafter", "ansi(r,Hello\\, world \\(hi\\)%b)after", "")]
	[Arguments("%ch%cr<<%cn %xh%xg>>%cn", "ansi(hr,<<)%b[ansi(hg,>>)]", "")]
	[Arguments("%c<#ff0000>R%C<#0000ff>B%cn", "ansi(#ff0000,R)[ansi(#ff0000 /#0000ff,B)]", "")]
	[Arguments("%cR%cwBG%cn plain", "ansi(wR,BG)%bplain", "")]
	[Arguments("%crA%rB%cn", "ansi(r,A%rB)", "")]
	[Arguments("\\%crA", "\\%crA", "")]
	[Arguments("%crA[space(3)]B%cn", "ansi(r,A[space(3)]B)", "")]
	[Arguments("%cu%cgunder%cn %ci%cyinv%cn %cf%cbflash%cn", "ansi(ug,under)%b[ansi(iy,inv)]%b[ansi(fb,flash)]", "")]
	[Arguments("%crA,B%cn", "ansi(r,A\\,B)", "")]
	[Arguments("%crfoo(x)%cn", "ansi(r,foo\\(x\\))", "")]
	[Arguments("%ch%crHi %cgthere%cn", "ansi(hr,Hi%b)[ansi(hg,there)]", "")]
	[Arguments("%cr  two  spaces  %cn|", "ansi(r,%btwo  spaces%b)|", "")]
	[Arguments("%cr[setq(0,abc)]%q0%cn", "ansi(r,[setq(0,abc)]%q0)", "")]
	[Arguments("[iter(a b c,%cr##%cn)]", "[iter(a b c,ansi(r,##))]", "")]
	[Arguments("switch(2,1,%crone%cn,2,%cgtwo%cn)", "switch(2,1,ansi(r,one),2,ansi(g,two))", "")]
	[Arguments("%cr[ansi(g,G)]Y", "ansi(r,[ansi(g,G)]Y)", "")]
	[Arguments("[setq(0,%cr)]%q0X%cn", "[setq(0,)]%q0X", "colour-code-in-register trailing-codes-dropped")]
	[Arguments("%cr[if(1,x%cny)]z", "ansi(r,[if(1,xy)]z)", "inner-reset-under-open-colour")]
	[Arguments("left(%crabc,1)Z", "left(ansi(r,abc),1)Z", "colour-leaks-out-of-call")]
	[Arguments("[ljust(%crab%cn,5)]|", "[ljust(ansi(r,ab),5)]|", "")]
	[Arguments("%x<#00ff00>green%xn", "ansi(#00ff00,green)", "")]
	[Arguments("%crabc%cqdef%cn", "ansi(r,abc\\%cqdef)", "")]
	[Arguments("%ch%cx%cWblack on white%cn", "ansi(hx W,black on white)", "")]
	[Arguments("%c0x46xterm%cn plain", "ansi(+xterm70,xterm)%bplain", "")]
	[Arguments("[setq(y,r)]%c%qyN/A%cn", "[setq(y,r)][ansi(%qy,N/A)]", "colour-code-from-register")]
	[Arguments("%ch%c%qyBold%cn", "ansi(h%qy,Bold)", "colour-code-from-register")]
	public async Task RewritesTheRhostBattery(string body, string expected, string flags)
	{
		var result = ColourSubstitutionRewriter.Rewrite(body, RhostExpression);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(string.Join(' ', result.Flags.Order(StringComparer.Ordinal))).IsEqualTo(flags);
		await Assert.That(result.Changed).IsEqualTo(body != expected);
	}

	/// <summary>
	/// The one place the dialects part: TinyMUX drops a letter it does not know, RhostMUSH prints the code
	/// as written.
	/// </summary>
	[Test]
	[Arguments(ColourSubstitutionDialect.TinyMux, "ansi(r,abcqdef)")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "ansi(r,abc\\%cqdef)")]
	public async Task AnUnknownCodeFollowsTheDialect(ColourSubstitutionDialect dialect, string expected)
	{
		var result = ColourSubstitutionRewriter.Rewrite("%crabc%cqdef%cn", new ColourRewriteOptions(dialect, ColourRewriteMode.Expression));

		await Assert.That(result.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task TheShortFormIsTheTinyMuxDialectAndLeavesPlainTextAlone()
	{
		await Assert.That(ColourSubstitutionRewriter.Rewrite("%crRED%cnplain")).IsEqualTo("ansi(r,RED)plain");
		await Assert.That(ColourSubstitutionRewriter.Rewrite("think [name(me)]")).IsEqualTo("think [name(me)]");
	}

	/// <summary>A command list keeps its pattern and rewrites only the actions after the colon.</summary>
	[Test]
	[Arguments("$+hi *:think %crHi %0%cn", "$+hi *:think ansi(r,Hi %0)")]
	[Arguments("@pemit %#=%crHi%cn", "@pemit %#=ansi(r,Hi)")]
	public async Task RewritesCommandLists(string body, string expected)
	{
		var result = ColourSubstitutionRewriter.Rewrite(body, new ColourRewriteOptions(ColourSubstitutionDialect.RhostMush));

		await Assert.That(result.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task LeavesAnUnbalancedBodyAlone()
	{
		var result = ColourSubstitutionRewriter.Rewrite("%cr[name(me)", RhostExpression);

		await Assert.That(result.Text).IsEqualTo("%cr[name(me)");
		await Assert.That(result.Flags).Contains(ColourSubstitutionRewriter.UnbalancedLeftAlone);
	}
}
