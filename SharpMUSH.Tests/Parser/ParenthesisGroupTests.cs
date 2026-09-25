using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// With <c>paren_groups</c> on, a <c>(</c> that does not start a function call opens a literal group (#1157). PennMUSH's
/// <c>process_expression</c> (<c>src/parse.c</c>, <c>case '('</c>) copies the <c>(</c>, evaluates up to
/// the matching <c>)</c> with only <c>PT_PAREN</c> as a terminator, and copies the <c>)</c>: commas
/// inside the group are text, and its <c>)</c> does not close the enclosing call. Expected values are
/// from a live PennMUSH (<c>95ad3511d</c>) where the issue gives one. With it off, the default, a
/// literal parenthesis inside a function's arguments is escaped.
/// </summary>
public class ParenthesisGroupTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));

	private async Task<string> Evaluate(string code, bool parenGroups = true)
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { ParenGroups = parenGroups }
		});
		return (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();
	}

	/// <summary>
	/// Off, a bare <c>(</c> is text and a <c>)</c> closes the call around it, so a literal parenthesis
	/// inside a function's arguments is written escaped.
	/// </summary>
	[Test]
	[Arguments("[cat(x,(a)b)]", "x (ab)")]
	[Arguments("[cat(x,(a,b)c)]", "x (a bc)")]
	[Arguments(@"[cat(x,\(a\,b\)c)]", "x (a,b)c")]
	[Arguments("[cat(x,%(a%,b%)c)]", "x (a,b)c")]
	[Arguments("[reswitch(abc,%(a%)%(b%)c,$1$2)]", "ab")]
	public async Task EscapedWhenOff(string code, string expected)
		=> await Assert.That(await Evaluate(code, parenGroups: false)).IsEqualTo(expected);

	[Test]
	[Arguments("[cat(x,(a)b)]", "x (a)b")]
	[Arguments("[cat(x,(a))]", "x (a)")]
	[Arguments("[strlen((a)b)]", "4")]
	[Arguments("[reswitch(abc,(abc),x)]", "x")]
	public async Task LeadingParenthesisIsText(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH answers <c>X,(A)B</c> here, because a comma in the final argument of a function that takes
	/// the rest of the line is kept (<c>PT_NOT_COMMA</c>). That is a separate rule; what this pins is
	/// the split, which no longer leaves <c>b)</c> behind.
	/// </summary>
	[Test]
	public async Task GroupEndsWhereItsParenthesisCloses()
		=> await Assert.That(await Evaluate("[ucstr(x,(a)b)]"))
			.IsEqualTo("#-1 FUNCTION (UCSTR) EXPECTS AT MOST 1 ARGUMENTS BUT GOT 2");

	[Test]
	[Arguments("[cat(x,(a,b)c)]", "x (a,b)c")]
	[Arguments("[cat(x,((a)b)c)]", "x ((a)b)c")]
	[Arguments("[cat(x,( a )b)]", "x ( a )b")]
	// A name after text is not a call, so its parenthesis is a group too.
	[Arguments("[cat(x,y add(1,2) z)]", "x y add(1,2) z")]
	// A bracket inside a group still evaluates, and its call's commas still split.
	[Arguments("[cat(x,([add(1,2)]),y)]", "x (3) y")]
	[Arguments("[cat(x,(%q<a>))]", "x ()")]
	public async Task GroupContents(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	[Arguments("(a)b", "(a)b")]
	[Arguments("a) b", "a) b")]
	[Arguments(":( sad", ":( sad")]
	[Arguments("x foo(1,2) y", "x foo(1,2) y")]
	public async Task TopLevelText(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	/// <summary>The regexp patterns #1156's tests had to escape.</summary>
	[Test]
	[Arguments("[reswitch(abc,(a)(b)c,$1$2)]", "ab")]
	[Arguments("[reswitch(abc,(?<first>a)(b)c,$<first>)]", "a")]
	[Arguments("[reswitch(abc,(a)(b)c,[reswitch(xyz,x(y)z,$1)])]", "y")]
	[Arguments("[regmatch(abc,(a)(b)c)]", "1")]
	public async Task RegexpPatterns(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	/// <summary>
	/// The editor's parses split arguments as evaluation does: with the option on, a comma inside a
	/// group is text in the semantic tokens, as it is when the code runs.
	/// </summary>
	[Test]
	public async Task SemanticTokensSeeGroups()
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { ParenGroups = true }
		});

		var commas = Parser.GetSemanticTokens(MarkupText.Plain("cat(x,(a,b)c)"))
			.Where(token => token.Text == ",")
			.Select(token => token.TokenType)
			.ToList();

		await Assert.That(commas).IsEquivalentTo([SemanticTokenType.Operator, SemanticTokenType.Text]);
	}
}
