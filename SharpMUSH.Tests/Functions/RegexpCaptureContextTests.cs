using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>reswitch()</c> evaluates its matched body inside a regexp capture context, and <c>$&lt;digit&gt;</c>
/// and <c>$&lt;name&gt;</c> read that context while the body is evaluated (#1156). PennMUSH's
/// <c>fun_reswitch</c> (<c>src/funmisc.c</c>) fills the context with <c>pe_regs_set_rx_context</c> and
/// evaluates the body with <c>PE_DOLLAR</c>; the evaluator's <c>'$'</c> case (<c>src/parse.c</c>) reads
/// only the innermost regexp context, and a new attribute (<c>PE_REGS_NEWATTR</c>) hides the caller's.
/// </summary>
/// <remarks>
/// Named groups escape their parentheses, as a literal <c>(</c> inside a function's arguments must be
/// unless <c>paren_groups</c> is on (#1157).
/// </remarks>
public class RegexpCaptureContextTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	/// <summary>
	/// A root state of its own: the shared function parser's register stacks are shared by every test
	/// running beside this one, and a capture frame on them would be visible to those tests.
	/// </summary>
	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));

	private async Task<string> Evaluate(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	/// <summary>
	/// A capture is text. Pasting it into the body before evaluation ran matched user input as
	/// softcode; PennMUSH returns the capture as it was matched.
	/// </summary>
	[Test]
	[Arguments("[reswitch(lit([add(1,1)]),.+,$0)]", "[add(1,1)]")]
	[Arguments("[reswitch(lit([add(1,1)]),^%(?<x>.+%)$,$<x>)]", "[add(1,1)]")]
	[Arguments("[reswitchall(lit([add(1,1)]),.+,$0)]", "[add(1,1)]")]
	[Arguments("[reswitch(lit([setq(rx1156,pwned)]),.+,$0)]-[r(rx1156)]", "[setq(rx1156,pwned)]-")]
	public async Task CaptureIsNotEvaluated(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	[Arguments("[reswitch(abc,a(b)c,$0/$1)]", "abc/b")]
	[Arguments("[reswitch(abc,a(b)c,$2)]", "")]
	// A single digit follows the '$', as in PennMUSH; $10 is $1 then 0.
	[Arguments("[reswitch(abc,a(b)c,$10)]", "b0")]
	[Arguments("[reswitch(abc,^%(?<first>a%)bc,$<first>)]", "a")]
	[Arguments("[reswitch(abc,^%(?<first>a%)bc,$<FIRST>)]", "a")]
	[Arguments("[reswitch(abc,^%(?<first>a%)bc,$<missing>)]", "")]
	// The name is evaluated, up to the '>'.
	[Arguments("[reswitch(abc,^%(?<first>a%)bc,$<[lcstr(FIRST)]>)]", "a")]
	// '$' not followed by a digit or '<' is itself.
	[Arguments("[reswitch(abc,a(b)c,$ $x)]", "$ $x")]
	// An escaped '$' is literal.
	[Arguments("[reswitch(abc,a(b)c,\\$1)]", "$1")]
	public async Task DollarReadsTheCaptureContext(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	[Arguments("$0 $<x>", "$0 $<x>")]
	[Arguments("[strlen($0)]", "2")]
	// Only the '$' is literal: what follows it is evaluated as usual (PennMUSH src/parse.c:2342).
	[Arguments("$<[add(1,1)]>", "$<2>")]
	// A literal argument keeps its text, even inside a capture context (PE_LITERAL, src/parse.c:2355).
	[Arguments("[reswitch(abc,a(b)c,lit($1 $<x>))]", "$1 $<x>")]
	// %$0 is the switch text, not a capture.
	[Arguments("[reswitch(abc,a(b)c,%$0)]", "abc")]
	public async Task DollarIsLiteralWithoutAContext(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	// The innermost context shadows the outer one, and the outer returns when it ends.
	[Arguments("[reswitch(abc,a(b)c,[reswitch(xyz,x(y)z,$1)]$1)]", "yb")]
	// A nested reswitch that falls to its default still owns the innermost context, which is empty.
	[Arguments("[reswitch(abc,a(b)c,[reswitch(xyz,q,no,<$1>)])]", "<>")]
	// Without any context the default sees a literal '$1'.
	[Arguments("[reswitch(abc,q,no,$1)]", "$1")]
	// reswitchall rebinds the context for each match.
	[Arguments("[reswitchall(abc,a(b),$1,b(c),$1)]", "bc")]
	public async Task ContextsNest(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	[Arguments("[reswitch(abc,b,#$)]", "abc")]
	[Arguments("[reswitch(abc,q,no,#$)]", "abc")]
	public async Task HashDollarIsTheSubject(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	public async Task RegistersListsTheCaptureContext()
		=> await Assert.That(await Evaluate("[reswitch(abc,^%(?<first>a%)%(b%)c,registers(,regexp,|))]"))
			.IsEqualTo("0|1|2|FIRST");

	/// <summary>
	/// PennMUSH marks a u()'d attribute <c>PE_REGS_NEWATTR</c>, and <c>pi_regs_get_rx</c> stops there:
	/// the caller's captures are not the called attribute's. A reswitch inside that attribute has its own.
	/// </summary>
	[Test]
	public async Task CalledAttributeDoesNotSeeTheCallersCaptures()
	{
		await Evaluate("[attrib_set(me/RX_NEWATTR_1156,lit($1))][attrib_set(me/RX_OWN_1156,lit([reswitch(xyz,x(y)z,$1)]))]");

		await Assert.That(await Evaluate("[reswitch(abc,a(b)c,[u(me/RX_NEWATTR_1156)]/[u(me/RX_OWN_1156)]/$1)]"))
			.IsEqualTo("$1/y/b");
	}

	/// <summary>
	/// A wildcard <c>switch()</c> records its captures as a regexp context too: <c>fun_switch</c> localizes
	/// <c>PE_REGS_SWITCH | PE_REGS_CAPTURE</c>, and <c>local_wild_match</c> stores one capture per
	/// <c>*</c> or <c>?</c>, numbered from 0 (<c>src/wild.c</c>). Rows marked Penn are from the live oracle
	/// in the issue.
	/// </summary>
	[Test]
	[Arguments("[switch(foo,f*,$0)]", "oo")] // Penn
	[Arguments("[switch([lit([add(1,1)])],*,$0)]", "[add(1,1)]")] // Penn
	[Arguments("[switch(foo,f*,[setr(x,$0)])]", "oo")] // Penn
	[Arguments("[reswitch(abc,a(b)c,[switch(xyz,x*,$0)])]", "yz")] // Penn
	[Arguments("[switch(foo,f*,[registers(,regexp)])]", "0")] // Penn
	[Arguments("[setq(alpha,1)][switch(foo,f*,registers())]", "ALPHA 0 0")] // Penn
	[Arguments("[switch(foo,foo,registers(,regexp))]", "")] // Penn
	[Arguments("[switch(foo,foo,$0)]", "$0")]
	[Arguments("[switch(abc,a?c,$0)]", "b")]
	[Arguments("[switch(a-b-c,*-*-*,$2$1$0)]", "cba")]
	[Arguments("[switch(ABC,a*,$0)]", "BC")]
	[Arguments("[switch(foo,x*,no,$0)]", "$0")]
	[Arguments("[switchall(foo,f*,$0,*o,$0)]", "oofo")]
	public async Task SwitchRecordsWildcardCaptures(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	/// <summary>
	/// <c>switch()</c> takes its arguments as (pattern, list) pairs, with a default only when one is left
	/// over (<c>fun_switch</c>: <c>j += 2</c>, and a default when <c>nargs</c> is even). A list is never
	/// tried as a pattern, so it is never evaluated unless its own pattern matched.
	/// </summary>
	[Test]
	[Arguments("[switch(b,a,b,c,d,e)]", "e")]
	[Arguments("[switch(foo,bar,1)]", "")]
	[Arguments("[switch(x,y,[setq(se1156,1)],z)]-[r(se1156)]", "z-")]
	[Arguments("[switchall(b,a,b,c,d)]", "")]
	public async Task SwitchPairsPatternsWithLists(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	/// <summary>
	/// <c>case()</c> is <c>fun_switch</c> with an exact, case-sensitive match: the same pairing, a switch
	/// context for <c>%$0</c>, and no regexp context of its own, so <c>$1</c> reads the enclosing one.
	/// </summary>
	[Test]
	[Arguments("[case(b,a,b,c,d,e)]", "e")]
	[Arguments("[case(foo,bar,1)]", "")]
	[Arguments("[caseall(b,a,b,c,d)]", "")]
	[Arguments("[case(foo,foo,%$0)]", "foo")]
	[Arguments("[case(Foo,foo,1,0)]", "0")]
	[Arguments("[reswitch(abc,a(b)c,[case(x,x,$1)])]", "b")]
	public async Task CaseMatchesExactly(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);
}
