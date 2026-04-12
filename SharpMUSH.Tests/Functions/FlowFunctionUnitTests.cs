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

}