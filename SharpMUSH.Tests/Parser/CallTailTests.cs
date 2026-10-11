namespace SharpMUSH.Tests.Parser;

/// <summary>
/// The text after a call that starts a string (the grammar's <c>callTail</c>). It is not the start of
/// anything, so a <c>name(</c> there is text, as it is after any other text: <c>add(1,2)mul(2,3)</c> is
/// <c>3mul(2,3)</c>, as in PennMUSH, whose <c>process_expression</c> checks for a function only at the
/// first <c>(</c>. It used to reuse the rule for the start of a string, which cannot begin with a
/// <c>name(</c>, so the parser refused the token and answered <c>#-1 PARSER FAILURE</c>.
/// </summary>
public class CallTailTests : ServerTestBase
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
	[Arguments("add(1,2)mul(2,3)", "3mul(2,3)")]
	// Whether the name is a function makes no difference: it is not looked up.
	[Arguments("add(1,2)abc(x)", "3abc(x)")]
	[Arguments("add(1,2)abc(x)def(y)", "3abc(x)def(y)")]
	[Arguments("add(1,2)mul(", "3mul(")]
	[Arguments("@@(a note)add(1,2)", "add(1,2)")]
	[Arguments("lcstr(ABC)lcstr(DEF)", "abclcstr(DEF)")]
	// A bracket in the tail still evaluates, before or after the text.
	[Arguments("add(1,2)mul(2,3)[mul(2,3)]", "3mul(2,3)6")]
	[Arguments("add(1,2)[mul(2,3)]mul(2,3)", "36mul(2,3)")]
	[Arguments("add(1,2)mul([add(1,1)],3)", "3mul(2,3)")]
	// So do substitutions, and an escape keeps a character as text.
	[Arguments("add(1,2)mul(%#)", "3mul(#1)")]
	[Arguments("add(1,2)\\mul(2,3)", "3mul(2,3)")]
	// Braces keep what they hold as written.
	[Arguments("add(1,2){mul(2,3)}", "3mul(2,3)")]
	// A space after the call is kept; one at the end is not.
	[Arguments("add(1,2) mul(2,3)", "3 mul(2,3)")]
	[Arguments("add(1,2) mul(2,3) ", "3 mul(2,3)")]
	[Arguments("add(1,2)  mul(2,3)", "3 mul(2,3)")]
	// Inside brackets the tail is the same text.
	[Arguments("[add(1,2)mul(2,3)]", "3mul(2,3)")]
	[Arguments("x[add(1,2)mul(2,3)]y", "x3mul(2,3)y")]
	// Inside braces nothing is a call, so there is no tail.
	[Arguments("{add(1,2)mul(2,3)}", "add(1,2)mul(2,3)")]
	public async Task ANameAfterALeadingCallIsText(string code, string expected)
		=> await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(expected);

	[Test]
	// In an argument the tail's ',' still splits the call around it and its ')' still closes it, as text
	// after any other text does; the call's own ')' is then text.
	[Arguments("cat(x,add(1,2)mul(2,3))", "x 3mul(2 3)")]
	[Arguments("if(1,add(1,2)mul(2 3))", "3mul(2 3)")]
	[Arguments("strlen(add(1,2)abc(x))", "6)")]
	[Arguments("iter(1 2,add(%i0,1)x)", "2x 3x")]
	[Arguments("cat(add(1,2)abc,def)", "3abc def")]
	// Escaped, they are text.
	[Arguments("cat(x,add(1,2)mul\\(2\\,3\\))", "x 3mul(2,3)")]
	public async Task InAnArgumentTheTailEndsWhereTheArgumentDoes(string code, string expected)
		=> await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(expected);

	[Test]
	// With paren_groups on, the name's '(' opens a group, whose ',' and ')' are its own.
	[Arguments("add(1,2)mul(2,3)", "3mul(2,3)")]
	[Arguments("cat(x,add(1,2)mul(2,3))", "x 3mul(2,3)")]
	[Arguments("cat(x,add(1,2)mul(2,3),y)", "x 3mul(2,3) y")]
	[Arguments("[add(1,2)mul(2,3)]", "3mul(2,3)")]
	[Arguments("add(1,2)mul([add(1,1)],3)", "3mul(2,3)")]
	// A group left open runs to the end of the text, outside a call.
	[Arguments("add(1,2)mul(2,3", "3mul(2,3")]
	public async Task WithParenGroupsTheNameOpensAGroup(string code, string expected)
		=> await Assert.That(await Evaluate(code, parenGroups: true)).IsEqualTo(expected);

	[Test]
	[Arguments("think add(1,2)mul(2,3)", "3mul(2,3)")]
	[Arguments("think add(1,2)abc(x) and more", "3abc(x) and more")]
	[Arguments("think @@(a note)add(1,2)", "add(1,2)")]
	[Arguments("think [add(1,2)mul(2,3)]", "3mul(2,3)")]
	public async Task ACommandArgumentKeepsItsTail(string command, string expected)
		=> await Assert.That(await Cmd(command)).IsEqualTo(expected);

	[Test]
	[Arguments("add(1,2)mul(2,3)")]
	[Arguments("u(me/X)text(more)")]
	[Arguments("@@(note)add(1,2)")]
	[Arguments("[add(1,2)mul(2,3)]")]
	public async Task TypedAssignmentStoresTheWholeValue(string value)
	{
		var attribute = "TAIL" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

		await Cmd($"&{attribute} me={value}");

		await Assert.That(await Eval($"get(me/{attribute})")).IsEqualTo(value)
			.Because("a value that parses whole is stored whole");
	}

	[Test]
	public async Task AStoredTailEvaluatesWhenCalled()
	{
		var attribute = "TAIL" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		await Cmd($"&{attribute} me=add(%0,1)mul(%0)");

		await Assert.That(await Eval($"u(me/{attribute},4)")).IsEqualTo("5mul(4)");
	}
}
