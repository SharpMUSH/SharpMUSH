using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class SFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("s(hello)", "hello")]
	[Arguments("s([add(1,2)])", "3")]
	[Arguments("s(  hello  )", "hello")]
	[Arguments("s([ljust(a,5)])", "a")]
	public async Task SFunction(string input, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("objeval(#1,add(1,2))", "3")]
	[Arguments("objeval(#1,num(me))", "#1")]
	public async Task ObjevalFunction(string input, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
}
