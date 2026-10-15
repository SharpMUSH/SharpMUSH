namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A bracket starts over: only its <c>]</c> ends it. PennMUSH's <c>process_expression</c> reads what a
/// <c>[</c> holds with <c>PT_BRACKET</c> as its only terminator, so a <c>,</c> <c>)</c> <c>;</c> or
/// <c>=</c> inside one is text, even when the bracket sits in a function's argument or a command. The
/// grammar used to carry the call around the bracket into it, so <c>cat(a,[x,y])</c> was
/// <c>#-1 PARSER FAILURE</c>. Expected values are from a live PennMUSH (<c>95ad3511d</c>).
/// </summary>
public class BracketStartsOverTests : ServerTestBase
{
	private async Task<string> Evaluate(string code, bool parenGroups)
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { ParenGroups = parenGroups }
		});
		return await Eval(code);
	}

	[Test]
	[Arguments("cat(a,[x)])", "a x)")]
	[Arguments("cat(a,[x,y])", "a x,y")]
	[Arguments("cat(a,[x=y])", "a x=y")]
	[Arguments("cat(a,[x;y])", "a x;y")]
	[Arguments("cat(a,[cat(b,c))])", "a b c)")]
	[Arguments("cat(a,[x]y,z)", "a xy z")]
	[Arguments("cat(a,[cat(b,[x,y])],z)", "a b x,y z")]
	[Arguments("[x;y]", "x;y")]
	[Arguments("[x)]", "x)")]
	// Inside the bracket a call is a call again, and its own ',' and ')' are its own.
	[Arguments("cat(a,[add(1,2)],b)", "a 3 b")]
	// A brace inside the bracket still keeps what it holds as written.
	[Arguments("cat(a,[{x,y}])", "a x,y")]
	public async Task OnlyTheClosingBracketEndsIt(string code, string expected)
	{
		await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(expected);
		await Assert.That(await Evaluate(code, parenGroups: true)).IsEqualTo(expected);
	}

	/// <summary>
	/// The shape that found this: with <c>paren_groups</c> off, the default, a bare <c>(</c> is text and
	/// the first <c>)</c> closes the call, so <c>if(1,,(x))</c> is <c>)</c>. With it on, as in PennMUSH,
	/// <c>(x)</c> is a group and the empty second argument is the answer.
	/// </summary>
	[Test]
	[Arguments("if(1,,(x))", ")", "")]
	[Arguments("if(0,,(x))", "(x)", "(x)")]
	[Arguments("ifelse(1,,(x))", ")", "")]
	[Arguments("cat(a,,(x),b)", "a  (x,b)", "a  (x) b")]
	[Arguments("cat(a,[if(1,,(x))])", "a )", "a ")]
	public async Task AnEmptyArgumentBeforeAParenthesis(string code, string withoutGroups, string withGroups)
	{
		await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(withoutGroups);
		await Assert.That(await Evaluate(code, parenGroups: true)).IsEqualTo(withGroups);
	}

	[Test]
	[Arguments("think [x;y]", "x;y")]
	[Arguments("think cat(a,[x,y])", "a x,y")]
	[Arguments("think cat(a,[x)])", "a x)")]
	public async Task ACommandDoesNotSplitInsideABracket(string command, string expected)
		=> await Assert.That(await Cmd(command)).IsEqualTo(expected);

	/// <summary>A register name starts over the same way: only its <c>&gt;</c> ends it (PennMUSH's <c>PT_GT</c>).</summary>
	[Test]
	[Arguments("cat(a,%q<x,y>)", "a ")]
	[Arguments("cat(a,%q<x)y>)", "a ")]
	[Arguments("[setq(x,ok)][cat(a,%q<[ifelse(1,x,z)]>)]", "a ok")]
	public async Task OnlyTheClosingAngleEndsAName(string code, string expected)
	{
		await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(expected);
		await Assert.That(await Evaluate(code, parenGroups: true)).IsEqualTo(expected);
	}

	[Test]
	public async Task ACommandDoesNotSplitInsideAName()
		=> await Assert.That(await Cmd("think -%q<x;y>-")).IsEqualTo("--");
}
