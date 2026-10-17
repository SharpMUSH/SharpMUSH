using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MixFunctionCallShapeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("a b,1 2", "a1 b2")]
	[Arguments("a|b,1|2,|", "a1|b2")]
	[Arguments("a|b,1|2,x|y,|", "a1x|b2y")]
	[Arguments("a|b,1|2,x|y,X|Y,|", "a1xX|b2yY")]
	public async Task OptionalTailPassesAdditionalListsToTheCallbackAndUsesItsLastArgumentAsDelimiter(string arguments, string expected)
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "MixCallShape");
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&CB {obj}=%0%1%2%3"));
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"mix({obj}/CB,{arguments})"))
			?? throw new InvalidOperationException("MIX returned no parser result.");
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments(30, "29:29:30")]
	[Arguments(31, "30:30:31")]
	[Arguments(33, "32:32:33")]
	public async Task UpperListCountsSupplyTheHighestNumericArgumentAndExactCount(int listCount, string expected)
	{
		var highest = listCount - 1;
		var callback = $"%<{highest}>:[r({highest},args)]:%+";
		await Assert.That(await EvaluateWithCallback(callback, Lists(listCount))).IsEqualTo(expected);
	}

	[Test]
	public async Task ThirtyFourListsWithAnExplicitDelimiterExceedTheFunctionArity()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "MixAritySideEffect");
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&MARKER {obj}=unchanged"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&CB {obj}=set({obj},MARKER:called)"));

		var refused = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"mix({obj}/CB,{Lists(34)},|)"))
			?? throw new InvalidOperationException("MIX returned no parser result.");
		await Assert.That(refused.Message.ToPlainText())
			.IsEqualTo("#-1 FUNCTION (MIX) EXPECTS AT MOST 35 ARGUMENTS BUT GOT 36");
		await Assert.That(await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"get({obj}/MARKER)")))
			.IsEqualTo(MarkupText.Plain("unchanged"));

		// The same callback can write when admitted: the refusal above suppresses a real side effect.
		await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"mix({obj}/CB,{Lists(33)},|)"));
		await Assert.That(await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"get({obj}/MARKER)")))
			.IsEqualTo(MarkupText.Plain("called"));
	}

	[Test]
	public async Task ThirtyThreeListsPadTheLastListWithoutDroppingItsArgumentSlot()
	{
		var lists = Enumerable.Range(0, 32).Select(index => $"{index}|second").Append("last");
		await Assert.That(await EvaluateWithCallback("%0:%<32>:[r(32,args)]:[hasarg(32,supplied)]:%+", string.Join(',', lists)))
			.IsEqualTo("0:last:last:1:33|second:::1:33");
	}

	[Test]
	public async Task ThirtyThreeListsAlsoReachTheLambdaEvaluationPath()
	{
		var result = await Factory.FunctionParser.FunctionParse(
			MarkupText.Plain($"mix(lit(#lambda/%<32>:[r(32,args)]:%+),{Lists(33)},|)"))
			?? throw new InvalidOperationException("MIX returned no parser result.");
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(result.Message.ToPlainText()).IsEqualTo("32:32:33");
	}

	private static string Lists(int count) => string.Join(',', Enumerable.Range(0, count));

	private async Task<string> EvaluateWithCallback(string callback, string lists)
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "MixListBoundary");
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&CB {obj}={callback}"));
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"mix({obj}/CB,{lists},|)"))
			?? throw new InvalidOperationException("MIX returned no parser result.");
		await Assert.That(result.HadErrors).IsFalse();
		return result.Message.ToPlainText();
	}
}
