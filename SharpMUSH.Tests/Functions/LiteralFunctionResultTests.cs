namespace SharpMUSH.Tests.Functions;

public class LiteralFunctionResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(@"plaintextwrapper([ulambda(#lambda/\[)])", true)]
	[Arguments("plaintextwrapper([add(1,2)])", false)]
	[Arguments("plaintextwrapper([lit(#-1 EXCEPTION: ordinary text)])", false)]
	[Arguments(@"plaintextwrapper(ulambda(#lambda/\[))", false)]
	public async Task LiteralWrapperRetainsFailuresOnlyFromEvaluatedBrackets(string expression, bool expected)
	{
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.HadErrors).IsEqualTo(expected);
		if (expression.Contains("add(1,2)")) await Assert.That(result.Message!.Text).IsEqualTo("plaintextwrapper(3)");
	}
}
