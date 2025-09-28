using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests 
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"u(#lambda/add\(1\,2\))", "3")]
	// CONSIDER: 3) is not the correct return value. This should just be: 3
	// However, this is how our parser should handle this. 
	[Arguments("u(lit(#lambda/add(1,2)))", "3)")] 
	[Arguments("u(#lambda/[add(1,2)])", "3")]
	[Arguments("u(#lambda/3)", "3")]
	[Arguments("3", "3")] 
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await Parser!.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}