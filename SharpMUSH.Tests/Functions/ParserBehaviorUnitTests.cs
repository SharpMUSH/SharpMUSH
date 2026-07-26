using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ParserBehaviorUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// Penn lit.1-lit.6: lit() prevents evaluation
	[Test]
	[Arguments("lit(hello world)", "hello world")]
	[Arguments("lit([add(1,2)])", "[add(1,2)]")]
	[Arguments("lit(near       far)", "near       far")]
	[Arguments("lit(%b%b%b)", "%b%b%b")]
	[Arguments("lit({test})", "{test}")]
	[Arguments("lit()", "")]
	[Arguments("lit(add(1,2))", "add(1,2)")]
	[Arguments("lit(lit(hello))", "lit(hello)")]
	[Arguments("lit(%r)", "%r")]
	[Arguments("lit(%t)", "%t")]
	[Arguments("lit(|)", "|")]
	[Arguments("lit(;)", ";")]
	public async Task Lit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// lit() returns its argument untouched, and "untouched" includes markup. The content used
	/// to be rebuilt from GetText(), which concatenates token text and therefore returns the
	/// characters without the colour runs the source carried — so colour applied to text stored
	/// in an attribute was lost the moment it passed through lit().
	/// </summary>
	[Test]
	public async Task LitPreservesMarkup()
	{
		var coloured = (await Parser.FunctionParse(MModule.single("ansi(+red,test)")))?.Message!;
		await Assert.That(coloured.ToString()).Contains("[");

		// Equivalent to lit(<coloured text>) arriving from a stored attribute value.
		var source = MModule.multiple([MModule.single("lit("), coloured, MModule.single(")")]);

		var result = (await Parser.FunctionParse(source))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("test");
		await Assert.That(result.ToString()).Contains("[");
	}

	// Penn fn.1-fn.3: fn() calls functions by name
	[Test]
	[Arguments("fn(add,1,2)", "3")]
	[Arguments("fn(mul,3,4)", "12")]
	[Arguments("fn(cat,hello,world)", "hello world")]
	[Arguments("fn(mid,hello,1,3)", "ell")]
	[Arguments("fn(ADD,1,2)", "3")]
	[Arguments("fn(add,1,2,3)", "6")]
	public async Task Fn(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn fn.error: fn with bad function name
	[Test]
	[Arguments("fn(notafunction)", "#-1 FUNCTION (NOTAFUNCTION) NOT FOUND")]
	public async Task FnError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// A name that is not a function is only an error where PennMUSH sets
	/// PE_FUNCTION_MANDATORY, i.e. inside <c>[...]</c> (src/parse.c). Everywhere else the
	/// text is copied through, so ordinary prose containing parentheses survives evaluation.
	/// </summary>
	[Test]
	[Arguments("notafunction(bar)", "notafunction(bar)")]
	[Arguments("Hello there(friend)", "Hello there(friend)")]
	[Arguments("strcat(notafunction(1))", "notafunction(1)")]
	[Arguments("strcat(a,notafunction(1),b)", "anotafunction(1)b")]
	public async Task UnknownFunctionStaysLiteralOutsideBrackets(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[notafunction(bar)]", "#-1 FUNCTION (NOTAFUNCTION) NOT FOUND")]
	[Arguments("[NotAFunction(bar)]", "#-1 FUNCTION (NOTAFUNCTION) NOT FOUND")]
	[Arguments("strcat([notafunction(1)])", "#-1 FUNCTION (NOTAFUNCTION) NOT FOUND")]
	public async Task UnknownFunctionErrorsInsideBrackets(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// The contents of a non-call are still evaluated with function recognition off, exactly
	/// as PennMUSH clears PE_FUNCTION_CHECK before copying the parenthesised text through —
	/// so a nested call stays literal, but a bracket inside re-enables evaluation.
	/// </summary>
	[Test]
	[Arguments("notafunction(add(1,2))", "notafunction(add(1,2))")]
	[Arguments("notafunction([add(1,2)])", "notafunction(3)")]
	[Arguments("notafunction(a,b)", "notafunction(a,b)")]
	public async Task NonCallContentsEvaluateWithoutFunctionRecognition(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn compress tests: space compression in evaluation
	[Test]
	[Arguments("cat(a,  b)", "a b")]
	[Arguments("cat(a,   b,   c)", "a b c")]
	[Arguments("cat(  ,  )", " ")]
	[Arguments("repeat(x,3)", "xxx")]
	[Arguments("space(5)", "     ")]
	public async Task CompressAndSpaces(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn qreg_noparse: lit prevents qreg substitution
	[Test]
	[Arguments("cat(setr(0,test),lit(%q0))", "test %q0")]
	public async Task QregNoparse(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn testinsert.t: insert() alias for linsert()
	[Test]
	[Arguments("insert(a b c,0,X)", "a b c")]
	[Arguments("insert(a b c,1,X)", "X a b c")]
	[Arguments("insert(a b c,2,X)", "a X b c")]
	[Arguments("insert(a b c,3,X)", "a b X c")]
	[Arguments("insert(a b c,4,X)", "a b c")]
	[Arguments("insert(a b c,-1,X)", "a b c X")]
	[Arguments("insert(a b c,-2,X)", "a b X c")]
	[Arguments("insert(a b c,-3,X)", "a X b c")]
	[Arguments("insert(a b c,-4,X)", "a b c")]
	public async Task InsertAlias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("add(1,add(2,3))", "6")]
	[Arguments("add(1,mul(2,3))", "7")]
	[Arguments("cat(add(1,2),mul(3,4))", "3 12")]
	public async Task NestedFunctions(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ADD(1,2)", "3")]
	[Arguments("Add(1,2)", "3")]
	[Arguments("aDd(1,2)", "3")]
	public async Task FunctionCaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Brace handling — braces prevent evaluation but are stripped
	[Test]
	[Arguments("{hello}", "hello")]
	[Arguments("add({1},{2})", "3")]
	[Arguments("cat({[add(1,2)]},done)", "3 done")]
	public async Task BraceHandling(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Escape sequences — backslash escapes special chars
	[Test]
	[Arguments(@"strlen(\[)", "1")]
	[Arguments(@"strlen(\])", "1")]
	public async Task EscapeSequences(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Empty argument handling — PennMUSH treats func() as 1 empty arg for MinArgs>=1 functions
	[Test]
	[Arguments("if(,yes,no)", "no")]
	[Arguments("if(1,yes,no)", "yes")]
	[Arguments("if(0,yes,no)", "no")]
	[Arguments("strlen()", "0")]
	[Arguments("words()", "0")]
	[Arguments("trim()", "")]
	public async Task EmptyArgHandling(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
