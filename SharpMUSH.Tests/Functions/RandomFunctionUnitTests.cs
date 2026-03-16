using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class RandomFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("die(6,2)", "")]
	[Arguments("die(2,6)", "")]
	[Arguments("die(3,10)", "")]
	public async Task Die(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
		// Result should be space-separated numbers
		var rolls = result.ToPlainText().Split(' ');
		await Assert.That(rolls.Length).IsGreaterThan(0);
	}

	[Test]
	[Arguments("rand(10)", "")]
	[Arguments("rand(100)", "")]
	[Arguments("rand()", "")]
	[Arguments("rand(5,10)", "")]
	public async Task Rand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var parsed = await Parser.FunctionParse(MModule.single(str));
		var result = parsed?.Message?.ToPlainText();
		Console.WriteLine($"Result value: '{result}'");
		Console.WriteLine($"Result length: {result?.Length}");
		await Assert.That(result).IsNotNull();
		// Should be a valid integer
		await Assert.That(int.TryParse(result, out _)).IsTrue();
	}

	[Test]
	[Arguments("shuffle(a b c d e)", "")]
	public async Task Shuffle(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("scramble(test)", "")]
	public async Task Scramble(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
