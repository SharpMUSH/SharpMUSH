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
}
