using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests 
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments(@"u(#lambda/add\(1\,2\))", "3")]
	// [Arguments("u(lit(#lambda/add(1,2)))", "3")] TODO: Failing test -> #-2 I DON'T KNOW WHICH ONE YOU MEAN)
	[Arguments("u(#lambda/[add(1,2)])", "3")] 
	[Arguments("u(#lambda/3)", "3")]
	[Arguments("3", "3")] 
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await Parser!.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}