using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// The examples in the softcode skill's "Brackets" section, SharpMUSH.Guide's Code Style guide and
/// <c>help functions</c>: where a call evaluates without <c>[]</c>, where it needs them, and what an
/// unneeded or missing pair does.
/// </summary>
public class BracketEvaluationGuideTests : ServerTestBase
{
	[Test]
	// A call that starts the text, or is a whole argument of another call, evaluates on its own.
	[Arguments("add(1,2)", "3")]
	[Arguments("[add(1,2)]", "3")]
	[Arguments("add(mul(2,3),1)", "7")]
	[Arguments("add( mul(2,3), 1)", "7")]
	[Arguments("if(1, add(1,2))", "3")]
	[Arguments("if(1,add(1,2),mul(2,3))", "3")]
	[Arguments("iter(1 2,add(%i0,1))", "2 3")]
	// Text after a leading call is still evaluated: substitutions and later bracketed calls.
	[Arguments("add(1,2) apples", "3 apples")]
	[Arguments("setq(fruit,5)%q<fruit>", "5")]
	[Arguments("add(1,2)[mul(2,3)]", "36")]
	[Arguments("[add(1,2)]x[mul(2,3)]", "3x6")]
	[Arguments("@@(a note)[add(1,2)]", "3")]
	// A call after text, a substitution, a space or a paren is text unless bracketed.
	[Arguments("Total: add(1,2)", "Total: add(1,2)")]
	[Arguments("Total: [add(1,2)]", "Total: 3")]
	[Arguments("%#add(1,2)", "#1add(1,2)")]
	[Arguments("%#[add(1,2)]", "#13")]
	[Arguments(" add(1,2)", "add(1,2)")]
	[Arguments("%b add(1,2)", "  add(1,2)")]
	[Arguments("(add(1,2))", "(add(1,2))")]
	[Arguments("[@@(a note)]add(1,2)", "add(1,2)")]
	[Arguments("add (1,2)", "add (1,2)")]
	// Inside an argument, an unbracketed call after text keeps its commas and parens, which split
	// and close the outer call.
	[Arguments("iter(1 2,%i0: add(%i0,1))", ": add(1 2: add( 2)")]
	[Arguments("iter(1 2,%i0: [add(%i0,1)])", "1: 2 2: 3")]
	[Arguments("cat(a,b add(1,2))", "a b add(1 2)")]
	// Brackets around a substitution, or doubled, change nothing.
	[Arguments("[%#]", "#1")]
	[Arguments("[[add(1,2)]]", "3")]
	// An argument the function does not evaluate stays text, brackets included.
	[Arguments("lit(add(1,2))", "add(1,2)")]
	[Arguments("lit([add(1,2)])", "[add(1,2)]")]
	// An empty pair evaluates to nothing.
	[Arguments("A[[]x[]]B", "AxB")]
	// Braces in an argument stop recognition; brackets inside them start it again.
	[Arguments("if(1,{add(1,2)})", "add(1,2)")]
	[Arguments("if(1,{[add(1,2)]})", "3")]
	// A word that is a function's name, straight before a paren at the start, is a call; an unknown
	// name stays text, except inside brackets.
	[Arguments("Last(minute) changes are fine", "minute changes are fine")]
	[Arguments("\\Last(minute) changes are fine", "Last(minute) changes are fine")]
	[Arguments("Smile(s) all round", "Smile(s) all round")]
	[Arguments("[smile(s)]", "#-1 FUNCTION (SMILE) NOT FOUND")]
	public async Task Evaluates(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	// A command's argument is a fresh start: a call heading it evaluates, after text it does not.
	[Arguments("think add(1,2)", "3")]
	[Arguments("think [add(1,2)]", "3")]
	[Arguments("think Sum: add(1,2)", "Sum: add(1,2)")]
	[Arguments("think Sum: [add(1,2)]", "Sum: 3")]
	// help functions brackets
	[Arguments("think add(1,2) apples", "3 apples")]
	[Arguments("think Total: add(1,2)", "Total: add(1,2)")]
	[Arguments("think Total: [add(1,2)]", "Total: 3")]
	[Arguments("think add(1,2)[mul(2,3)]", "36")]
	[Arguments("think iter(1 2,%i0: [add(%i0,1)])", "1: 2 2: 3")]
	[Arguments("think cat(a,b add(1,2))", "a b add(1 2)")]
	[Arguments("think Last(minute) changes are fine", "minute changes are fine")]
	[Arguments("think \\Last(minute) changes are fine", "Last(minute) changes are fine")]
	public async Task CommandArguments(string command, string expected)
		=> await Assert.That(await Cmd(command)).IsEqualTo(expected);

	[Test]
	public async Task EachArgumentOfACommandStartsAfresh()
	{
		var attribute = "BRK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		await Cmd($"&{attribute}`STEP me=&{attribute}`OUT me=%0|%1|%2");

		await Cmd($"@include me/{attribute}`STEP= lcstr(ABC), lcstr(DEF), x lcstr(GHI)");

		await Assert.That(await Eval($"get(me/{attribute}`OUT)")).IsEqualTo("abc|def|x lcstr(GHI)");
	}

	[Test]
	public async Task AnUnbracketedCallStillEvaluatesWhenAnAttributeRunsAnAssignment()
	{
		var attribute = "BRK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		// Typed at a client, & stores its value as written, brackets or not.
		await Cmd($"&{attribute}`TYPED me=[add(1,2)]");
		await Cmd($"&{attribute}`STEP me=&{attribute}`OUT me=add(1,2);&{attribute}`KEPT me={{add(1,2)}}");

		await Cmd($"@include me/{attribute}`STEP");

		await Assert.That(await Eval($"get(me/{attribute}`TYPED)")).IsEqualTo("[add(1,2)]");
		await Assert.That(await Eval($"get(me/{attribute}`OUT)")).IsEqualTo("3")
			.Because("leaving the brackets off does not keep code from evaluating");
		await Assert.That(await Eval($"get(me/{attribute}`KEPT)")).IsEqualTo("add(1,2)")
			.Because("braces do");
	}

	[Test]
	public async Task AnAttributeBodyNeedsNoBrackets()
	{
		var attribute = "BRK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		await Cmd($"&{attribute}`BARE me=add(%0,1)");
		await Cmd($"&{attribute}`BRACKETED me=[add(%0,1)]");

		await Assert.That(await Eval($"u(me/{attribute}`BARE,4)")).IsEqualTo("5");
		await Assert.That(await Eval($"u(me/{attribute}`BRACKETED,4)")).IsEqualTo("5");
	}

	[Test]
	public async Task TheNotepadLineAppendsWithOneBracketPair()
	{
		var attribute = "BRK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		// The skill's @input notepad line: the leading v() needs no brackets, the if() after it does.
		await Cmd($"&{attribute}`LINE me=&{attribute}`DRAFT me=v({attribute}`DRAFT)[if(hasattr(me,{attribute}`DRAFT),%r)]%0");

		await Cmd($"@include me/{attribute}`LINE=first line");
		await Cmd($"@include me/{attribute}`LINE=second line");

		await Assert.That(await Eval($"get(me/{attribute}`DRAFT)")).IsEqualTo("first line\nsecond line");
	}

	[Test]
	public async Task TheHelpExampleBracketsOnlyTheSecondCall()
	{
		var attribute = "BRK" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		await Cmd($"&{attribute}`VA me=This is a");
		await Cmd($"&{attribute}`VB me=nice day");

		await Assert.That(await Eval($"first(rest(v({attribute}`VA) [v({attribute}`VB)]))")).IsEqualTo("is");
	}

	[Test]
	public async Task HelpBracketsOpensTheBracketsSection()
	{
		var help = await WebAppFactoryArg.Services.GetRequiredService<ITextFileService>().GetEntryAsync("help", "brackets");

		await Assert.That(help).Contains("A function is recognised without brackets in these places");
	}
}
