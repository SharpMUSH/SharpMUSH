using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FlowFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("if(1,True)", "True")]
	[Arguments("if(0,True)", "")]
	public async Task If(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}


	[Test]
	[Arguments("if(1,True,False)", "True")]
	[Arguments("if(0,True,False)", "False")]
	public async Task IfElse(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn null.1-null.3
	[Test]
	[Arguments("null()", "")]
	[Arguments("null(a)", "")]
	[Arguments("null(a,b,c)", "")]
	public async Task Null(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn letq.1: letq saves and restores registers around body
	[Test]
	[Arguments("setr(A, 1):[letq(A, 2, %qA)]:%qA", "1:2:1")]
	// Penn letq.2: letq with single arg (body only) does not save/restore
	[Arguments("setr(A, 1):[letq(setr(A, 2))]:%qA", "1:2:2")]
	public async Task LetQ(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Argument-parity enforcement. PennMUSH checks these inside the function bodies:
	/// fun_letq rejects <c>nargs % 2 != 1</c> and fun_setq rejects <c>nargs % 2 != 0</c>
	/// (src/funmisc.c). SharpMUSH declares the same rule as a function flag, but the
	/// dispatcher used to compare the whole flags value, so any function that combined
	/// the parity flag with another (letq carries NoParse) was never checked — letq would
	/// then read its own body as a register value.
	/// </summary>
	[Test]
	[Arguments("letq(A,1,B,2,%qA%qB)", "12")]
	[Arguments("letq(body)", "body")]
	[Arguments("letq(A,1,%qA)", "1")]
	public async Task ParityAcceptsValidArgumentCounts(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("letq(A,1)", "#-1 FUNCTION (LETQ) EXPECTS AN ODD NUMBER OF ARGUMENTS")]
	[Arguments("letq(A,1,B,2)", "#-1 FUNCTION (LETQ) EXPECTS AN ODD NUMBER OF ARGUMENTS")]
	[Arguments("setr(A,1,B)", "#-1 FUNCTION (SETR) EXPECTS AN EVEN NUMBER OF ARGUMENTS")]
	public async Task ParityRejectsInvalidArgumentCounts(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// CASE/CASEALL must NOT enforce parity: PennMUSH backs them with fun_switch, which
	/// checks only minargs, and the canonical form with a trailing default is even-numbered.
	/// </summary>
	[Test]
	[Arguments("case(b,a,first,b,second,fallback)", "second")]
	[Arguments("case(z,a,first,b,second,fallback)", "fallback")]
	[Arguments("case(a,a,first,b,second)", "first")]
	[Arguments("caseall(b,a,first,b,second,fallback)", "second")]
	public async Task CaseHasNoParityRequirement(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn firstof: returns first non-zero argument
	[Test]
	[Arguments("firstof(0,0,2)", "2")]
	[Arguments("firstof(2,0,0)", "2")]
	[Arguments("firstof(0,0,0)", "0")]
	[Arguments("firstof(1,2,3)", "1")]
	public async Task Firstof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn strfirstof: returns first non-empty-string argument
	[Test]
	[Arguments("strfirstof(,,foo)", "foo")]
	[Arguments("strfirstof(,bar,foo)", "bar")]
	[Arguments("strfirstof(bar,,foo)", "bar")]
	[Arguments("strfirstof(bar,baz,foo)", "bar")]
	public async Task StrFirstof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn allof: returns all non-zero args joined by separator (last arg)
	[Test]
	[Arguments("allof(0,0,2,)", "2")]
	[Arguments("allof(2,0,0,)", "2")]
	[Arguments("allof(0,0,0,)", "")]
	[Arguments("allof(1,2,3,%b)", "1 2 3")]
	[Arguments("allof(1,2,3,)", "123")]
	[Arguments("allof(0,0,2,@)", "2")]
	[Arguments("allof(2,0,0,@)", "2")]
	[Arguments("allof(0,0,0,@)", "")]
	[Arguments("allof(1,2,3,@)", "1@2@3")]
	public async Task Allof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn strallof: returns all non-empty-string args joined by separator (last arg)
	[Test]
	[Arguments("strallof(,,,@)", "")]
	[Arguments("strallof(foo,@)", "foo")]
	[Arguments("strallof(,foo,@)", "foo")]
	[Arguments("strallof(foo,,@)", "foo")]
	[Arguments("strallof(foo,bar,@)", "foo@bar")]
	[Arguments("strallof(,foo,bar,@)", "foo@bar")]
	[Arguments("strallof(foo,,bar,@)", "foo@bar")]
	[Arguments("strallof(foo,bar,,@)", "foo@bar")]
	public async Task StrAllof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

}