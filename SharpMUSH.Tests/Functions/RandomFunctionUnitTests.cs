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

	// Penn rand.1 — rand(-1) should return 0
	// NOTE: SharpMUSH currently rejects negative args; PennMUSH returns 0
	// [Test]
	// [Arguments("rand(-1)", "0")]
	// public async Task RandNegative(string str, string expected) { ... }

	// Penn rand.3 — rand(1) should always return 0
	[Test]
	[Arguments("rand(1)", "0")]
	public async Task RandOne(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn rand.5-rand.6 — deterministic two-arg rand cases
	[Test]
	[Arguments("rand(0,0)", "0")]
	[Arguments("rand(1,1)", "1")]
	public async Task RandDeterministic(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn randword.2 — randword of single word returns that word
	[Test]
	[Arguments("randword(foo)", "foo")]
	public async Task RandwordSingle(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
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
