using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FunctionResultPropagationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("and({0},1)", true)]
	[Arguments("strcat({0},x)", true)]
	[Arguments("cand({0},1)", true)]
	[Arguments("cor({0},1)", true)]
	[Arguments("cnand({0},1)", true)]
	[Arguments("ncand({0},1)", true)]
	[Arguments("ncor({0},0)", true)]
	[Arguments("reswitch(1,1,{0})", true)]
	[Arguments("reswitchall(1,1,{0},1,ok)", true)]
	[Arguments("reswitch(1,2,ok,{0})", true)]
	[Arguments("reswitch({0},.*,ok)", true)]
	[Arguments("reswitch(1,{0},ok,fallback)", true)]
	[Arguments("cand(0,{0})", false)]
	[Arguments("cor(1,{0})", false)]
	[Arguments("cnand(0,{0})", false)]
	[Arguments("ncand(0,{0})", false)]
	[Arguments("ncor(1,{0})", false)]
	[Arguments("reswitch(1,1,ok,{0})", false)]
	public async Task ActualNestedFailureRetainsMetadataOnlyWhenEvaluated(string expression, bool expected)
	{
		var code = expression.Replace("{0}", @"ulambda(#lambda/\[)");
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(code));
		await Assert.That(result!.HadErrors).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cand", "0")]
	[Arguments("cor", "1")]
	[Arguments("cnand", "1")]
	[Arguments("ncand", "1")]
	[Arguments("ncor", "0")]
	public async Task ErrorLookingLiteralRemainsOrdinaryText(string function, string expected)
	{
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"{function}(#-1 EXCEPTION: ordinary text,1)"));
		await Assert.That(result!.HadErrors).IsFalse();
		await Assert.That(result.Message!.Text).IsEqualTo(expected);
	}
}
