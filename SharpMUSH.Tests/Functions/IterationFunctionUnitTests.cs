namespace SharpMUSH.Tests.Functions;

public class IterationFunctionUnitTests : BaseUnitTest
{
	[Test]
	[Arguments("iter(1 2 3,%i0)", "1 2 3")]
	[Arguments("iter(1 2 3,add(%i0,1))", "2 3 4")]
	[Arguments("iter(1|2|3,%i0,|)", "1|2|3")]
	[Arguments("iter(1|2|3,%i0,|,-)", "1-2-3")]
	[Arguments("iter(1|2|3,add(%i0,1),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%i1)),|,-)", "2 3 4-3 4 5-4 5 6")]
	public async Task Iteration(string function, string expected)
	{
		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(function)))?.Message!;
	 	await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}