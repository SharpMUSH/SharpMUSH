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
	/// The same battery read as TinyMUX writes it, each run on a TinyMUX server before and after the rewrite.
	/// The rewrite writes an exact colour as SharpMUSH's <c>ansi()</c> reads it, <c>#rrggbb</c>, where the
	/// TinyMUX check used TinyMUX's <c>&lt;#rrggbb&gt;</c>.
	/// </summary>
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
	[Arguments("%crabc%cqdef%cn", "ansi(r,abcqdef)", "")]
	[Arguments("%ch%cx%cWblack on white%cn", "ansi(hx W,black on white)", "")]
	public async Task RewritesTheTinyMuxBattery(string body, string expected, string flags)
	{
		var result = ColourSubstitutionRewriter.Rewrite(body,
			new ColourRewriteOptions(ColourSubstitutionDialect.TinyMux, ColourRewriteMode.Expression));

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

	/// <summary>
	/// A call that picks a colour is only a colour when its unit holds nothing else. Beside text of its own
	/// it is text, and stays whole; one with no text after it is dropped and reported.
	/// </summary>
	[Test]
	[Arguments("[list(a,[iter(b,if(1,%c0x00,%c0xEF)%c0X[printf($02s,1)]Z,,)]%cn,,c)]",
		"[list(a,[iter(b,ansi(if(1,+xterm0,+xterm239),Z),,)],,c)]", "computed-colour-code-removed")]
	[Arguments("x(a,[iter(b,if(1,%cr,%cg)Z)]%cn,c)", "x(a,[iter(b,ansi(if(1,r,g),Z))],c)", "")]
	[Arguments("x(a,[if(1,%cr,%cg)]%cn,c)", "x(a,,c)", "trailing-codes-dropped")]
	public async Task KeepsACallThatPicksAColourBesideText(string body, string expected, string flags)
	{
		var result = ColourSubstitutionRewriter.Rewrite(body, RhostExpression);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(string.Join(' ', result.Flags.Order(StringComparer.Ordinal))).IsEqualTo(flags);
	}

	/// <summary>
	/// A code that cannot be rewritten is removed, since SharpMUSH would read <c>%c</c> as the last command. One
	/// whose colour comes from a call or a substitution goes with what it reads, whose result would print as text.
	/// TinyMUX prints what follows a code it cannot read, so removing the code alone is exact there; RhostMUSH
	/// printed the code too. A code at the end is a reset on both.
	/// </summary>
	[Test]
	[Arguments(ColourSubstitutionDialect.RhostMush, "A%c0X[printf($02s,pack(%0,16))]B", "AB", "computed-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "A%c[u(colour)]B", "AB", "computed-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "A%c%0B", "AB", "computed-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.TinyMux, "A%c[add(1,1)]B", "AB", "computed-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.TinyMux, "A%c%q0B", "AB", "computed-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "A%c B", "A B", "unconvertible-colour-code-removed")]
	[Arguments(ColourSubstitutionDialect.TinyMux, "A%c B", "A B", "")]
	[Arguments(ColourSubstitutionDialect.TinyMux, "A%c0x46B", "A0x46B", "")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "AB%c", "AB", "")]
	[Arguments(ColourSubstitutionDialect.RhostMush, "%c%q0A%cn", "ansi(%q0,A)", "colour-code-from-register")]
	public async Task RemovesACodeItCannotRewrite(ColourSubstitutionDialect dialect, string body, string expected, string flags)
	{
		var result = ColourSubstitutionRewriter.Rewrite(body, new ColourRewriteOptions(dialect, ColourRewriteMode.Expression));

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(string.Join(' ', result.Flags.Order(StringComparer.Ordinal))).IsEqualTo(flags);
	}

	[Test]
	public async Task LeavesAnUnbalancedBodyAlone()
	{
		var result = ColourSubstitutionRewriter.Rewrite("%cr[name(me)", RhostExpression);

		await Assert.That(result.Text).IsEqualTo("%cr[name(me)");
		await Assert.That(result.Flags).Contains(ColourSubstitutionRewriter.UnbalancedLeftAlone);
	}
}
