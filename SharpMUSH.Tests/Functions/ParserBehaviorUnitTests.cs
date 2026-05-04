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
}
