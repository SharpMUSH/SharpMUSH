using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Functions the suite registered, documented and never called. Regenerating the inventory for #974
/// left twenty-six of them; the vector family has its own class, and these are the rest.
///
/// <para>An untested function is not a gap in a report, it is behaviour nobody has ever run: the
/// first call written against half of these found something wrong with them.</para>
/// </summary>
public class UncoveredFunctionTests : ServerTestBase
{
	// ---- String and predicate functions, which need no world ------------------------------------

	[Test]
	[Arguments("spellnum(0)", "zero")]
	[Arguments("spellnum(5)", "five")]
	[Arguments("spellnum(21)", "twenty-one")]
	public async Task SpellNumSpellsANumberOut(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary><c>strdelete(&lt;string&gt;, &lt;first&gt;, &lt;len&gt;)</c>, zero-indexed as PennMUSH's is.</summary>
	[Test]
	[Arguments("strdelete(abcdefg,2,3)", "abfg")]
	[Arguments("strdelete(abcdefg,0,1)", "bcdefg")]
	[Arguments("strdelete(abcdefg,5,99)", "abcde")]
	public async Task StrDeleteRemovesASpanByPosition(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("stripaccents(déjà vu)", "deja vu")]
	[Arguments("stripaccents(plain)", "plain")]
	public async Task StripAccentsDowngradesToAscii(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("isword(abc)", "1")]
	[Arguments("isword(abc def)", "0")]
	[Arguments("isword(ab1)", "0")]
	public async Task IsWordAsksWhetherEveryCharacterIsALetter(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary><c>%[</c> is how softcode writes a literal bracket, which is what makes this pattern bad.</summary>
	[Test]
	[Arguments("isregexp(^a.*z$)", "1")]
	[Arguments("isregexp(%[)", "0")]
	public async Task IsRegexpAsksWhetherThePatternCompiles(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// The comparison family's one member nothing exercised. PennMUSH's <c>gte</c> is
	/// <c>fun_comp</c>'s <c>&gt;=</c>, so it compares numerically and is true when the two are equal.
	/// </summary>
	[Test]
	[Arguments("gte(2,1)", "1")]
	[Arguments("gte(1,1)", "1")]
	[Arguments("gte(1,2)", "0")]
	[Arguments("gte(1.5,1.4)", "1")]
	public async Task GreaterThanOrEqualIsNumericAndInclusive(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// <c>isdbref()</c> asks two questions at once: does the text parse as a dbref, and does that
	/// object exist. A well-formed reference to nothing is still 0.
	/// </summary>
	[Test]
	[Arguments("isdbref(#1)", "1")]
	[Arguments("isdbref(#2147483646)", "0")]
	[Arguments("isdbref(me)", "0")]
	[Arguments("isdbref(notadbref)", "0")]
	public async Task IsDbRefAsksWhetherTheReferenceResolves(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>The case-insensitive twin of reglmatchall(): the positions of every matching element.</summary>
	[Test]
	[Arguments("reglmatchalli(Apple banana APRICOT,^a)", "1 3")]
	[Arguments("reglmatchalli(Apple banana APRICOT,^z)", "")]
	public async Task RegLMatchAllIFindsEveryMatchingPositionIgnoringCase(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	// ---- Time ------------------------------------------------------------------------------------

	/// <summary>
	/// convutctime() reads a time string as UTC. Round-tripping it through utctime() is the check that
	/// does not depend on the machine's zone.
	/// </summary>
	[Test]
	public async Task ConvUtcTimeReadsATimeStringAsUtc()
		=> await Assert.That(await Eval("convutctime(Sat Jan 01 00:00:00 2000)")).IsEqualTo("946684800");

	[Test]
	public async Task UtcTimeAndConvUtcTimeRoundTrip()
	{
		var now = await Eval("utctime()");

		await Assert.That(await Eval($"sub(secs(),convutctime({now}))")).IsEqualTo("0");
	}

	// ---- Functions that need something in the world ----------------------------------------------

	[Test]
	public async Task HasAttrValAndHasAttrPValAskWhetherTheValueIsNonEmpty()
	{
		await Cmd("&COVER.FULL me=something");
		await Cmd("&COVER.EMPTY me=");

		await Assert.That(await Eval("hasattrval(me,COVER.FULL)")).IsEqualTo("1");
		await Assert.That(await Eval("hasattrval(me,COVER.EMPTY)")).IsEqualTo("0");
		await Assert.That(await Eval("hasattrval(me,COVER.MISSING)")).IsEqualTo("0");
		await Assert.That(await Eval("hasattrpval(me,COVER.FULL)")).IsEqualTo("1");
		await Assert.That(await Eval("hasattrpval(me,COVER.MISSING)")).IsEqualTo("0");
	}

	/// <summary>
	/// <c>pfun(&lt;attribute&gt;)</c> evaluates the attribute as inherited from the caller's @parent,
	/// ignoring the caller's own copy — which is the whole of the difference from ufun().
	/// </summary>
	[Test]
	public async Task PFunEvaluatesTheParentsCopyOfTheAttribute()
	{
		var parent = DBRef.Parse(TrailingDbref(await Cmd("@create CoverPfunParent")));
		var child = DBRef.Parse(TrailingDbref(await Cmd("@create CoverPfunChild")));
		await Cmd($"&COVER.PFUN #{parent.Number}=parent copy");
		await Cmd($"&COVER.PFUN #{child.Number}=child copy");
		await Cmd($"@parent #{child.Number}=#{parent.Number}");

		await Assert.That(await EvalAs(child, "pfun(COVER.PFUN)")).IsEqualTo("parent copy");
		await Assert.That(await EvalAs(child, "ufun(me/COVER.PFUN)")).IsEqualTo("child copy");
	}

	/// <summary>
	/// PennMUSH's fun_checkpass takes a player <em>name</em> (lookup_player); resolving the argument
	/// with a dbref parse alone answered #-1 NO SUCH PLAYER for every call that named one.
	/// </summary>
	[Test]
	public async Task CheckPassAcceptsAPlayerNameAsWellAsADbref()
	{
		var created = await Cmd("@pcreate CoverPassPlayer=hunter2");
		var dbref = created.TrimStart('#').Split(':')[0];

		await Assert.That(await Eval("checkpass(CoverPassPlayer,hunter2)")).IsEqualTo("1");
		await Assert.That(await Eval("checkpass(CoverPassPlayer,wrong)")).IsEqualTo("0");
		await Assert.That(await Eval($"checkpass(#{dbref},hunter2)")).IsEqualTo("1");
	}

	[Test]
	public async Task NChildrenCountsTheObjectsParentedToOne()
	{
		var parent = TrailingDbref(await Cmd("@create CoverParent"));
		var one = TrailingDbref(await Cmd("@create CoverChildOne"));
		var two = TrailingDbref(await Cmd("@create CoverChildTwo"));
		await Cmd($"@parent {one}={parent}");
		await Cmd($"@parent {two}={parent}");

		await Assert.That(await Eval($"nchildren({parent})")).IsEqualTo("2");
	}

	/// <summary>
	/// objmem() is a stub: <c>AttributeFunctions.cs:776</c> returns the literal <c>"0"</c> whatever it
	/// is asked about, so no caller can distinguish a large object from a small one. The helpfile says
	/// so too, now. This pins the stub rather than the arithmetic it does not do.
	/// </summary>
	[Test]
	public async Task ObjMemIsAStubAndAlwaysAnswersZero()
	{
		var thing = TrailingDbref(await Cmd("@create CoverMemObject"));
		await Cmd($"&COVER.BULK {thing}=0123456789");

		await Assert.That(await Eval($"objmem({thing})")).IsEqualTo("0");
	}

	[Test]
	public async Task LPlayersListsThePlayersInAContainer()
	{
		var room = await Eval("loc(me)");

		await Assert.That(await Eval($"lplayers({room})")).Contains("#1");
	}

	[Test]
	public async Task LPidsListsTheCallersQueuedProcesses()
	{
		await Cmd("@wait 3600=think CoverLPids");

		await Assert.That(await Eval("lpids()")).IsNotEmpty();
	}

	[Test]
	public async Task MudUrlReportsTheConfiguredAddress()
		=> await Assert.That(await Eval("mudurl()")).IsNotEqualTo(NullResult);

	/// <summary><c>xmwho(&lt;start&gt;, &lt;count&gt;)</c> — a window onto the non-hidden connected list.</summary>
	[Test]
	public async Task XmWhoWindowsTheConnectedList()
		=> await Assert.That(await Eval("xmwho(1,1)")).IsNotEqualTo(NullResult);

	[Test]
	public async Task CInfoReportsOneFieldOfAChannel()
	{
		await Cmd("@channel/add CoverInfoChannel=player");

		await Assert.That(await Eval("cinfo(CoverInfoChannel)")).IsEqualTo("CoverInfoChannel");
		await Assert.That(await Eval("cinfo(CoverInfoChannel,owner)")).IsEqualTo("#1");
		await Assert.That(await Eval("cinfo(CoverInfoChannel,nosuchfield)")).StartsWith("#-1");
	}

	/// <summary>The dbref a creation command reports, which it puts last in its answer.</summary>
	private static string TrailingDbref(string created) => created.Trim().Split(' ')[^1].Trim().TrimEnd('.');
}
