using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FlowFunctionUnitTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;

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
	
}