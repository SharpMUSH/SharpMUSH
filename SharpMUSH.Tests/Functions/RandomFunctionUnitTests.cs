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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
		var rolls = result.ToPlainText().Split(' ');
		await Assert.That(rolls.Length).IsGreaterThan(0);
	}

	[Test]
	[Arguments("rand(10)", "")]
	[Arguments("rand(100)", "")]
	[Arguments("rand(5,10)", "")]
	public async Task Rand(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var parsed = await Parser.FunctionParse(MarkupText.Plain(str));
		var result = parsed?.Message?.ToPlainText();
		TestDiagnostics.WriteLine($"Result value: '{result}'");
		TestDiagnostics.WriteLine($"Result length: {result?.Length}");
		await Assert.That(result).IsNotNull();
		await Assert.That(int.TryParse(result, out _)).IsTrue();
	}

	// Penn rand.1 — rand(-1) is 0; Penn rand.3 — rand(1) is always 0.
	[Test]
	[Arguments("rand(1)", "0")]
	[Arguments("rand(-1)", "0")]
	public async Task RandOne(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>fun_rand</c>'s whole range contract (<c>src/funmisc.c:777-830</c>), asserted as exact
	/// values or as membership of the entire reachable range, so no case here can pass or fail by
	/// luck of the draw.
	///
	/// <para><c>rand(2147483647, 2147483647)</c> is the one that used to throw: the implementation
	/// asked for <c>Random.Shared.Next(min, max + 1)</c>, and the unchecked increment wrapped to
	/// <c>int.MinValue</c>. <c>rand(5,1)</c> is the other correction — PennMUSH swaps a reversed
	/// pair (<c>:814-818</c>) rather than refusing it — and a negative single argument counts
	/// <em>down</em> from zero (<c>:800-806</c>) rather than answering 0.</para>
	/// </summary>
	[Test]
	[Arguments("rand(0)", new[] { "#-1 OUT OF RANGE" })]
	[Arguments("rand(x)", new[] { "#-1 ARGUMENT MUST BE INTEGER" })]
	[Arguments("rand(1.5)", new[] { "#-1 ARGUMENT MUST BE INTEGER" })]
	[Arguments("rand(1,x)", new[] { "#-1 ARGUMENTS MUST BE INTEGERS" })]
	[Arguments("rand(-2)", new[] { "-1", "0" })]
	[Arguments("rand(-3)", new[] { "-2", "-1", "0" })]
	[Arguments("rand(5,5)", new[] { "5" })]
	[Arguments("rand(-3,-3)", new[] { "-3" })]
	[Arguments("rand(5,1)", new[] { "1", "2", "3", "4", "5" })]
	[Arguments("rand(-1,1)", new[] { "-1", "0", "1" })]
	[Arguments("rand(2147483647,2147483647)", new[] { "2147483647" })]
	[Arguments("rand(-2147483648,-2147483648)", new[] { "-2147483648" })]
	public async Task RandRange(string str, string[] allowed)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToPlainText();

		await Assert.That(allowed).Contains(result!);
	}

	/// <summary>
	/// A negative single argument spans <c>[n+1, 0]</c> (<c>src/funmisc.c:800-806</c>). SharpMUSH
	/// short-circuited every negative argument to 0, which the range assertions above cannot catch
	/// because 0 is in the range. A hundred draws distinguish "spans the range" from "always 0"
	/// without asserting anything about the distribution: five values that come up 0 every time,
	/// a hundred times running, is one chance in 5^99.
	/// </summary>
	[Test]
	public async Task RandOfANegativeArgumentSpansItsWholeRange()
	{
		var seen = new HashSet<string>();
		for (var draw = 0; draw < 100; draw++)
		{
			seen.Add((await Parser.FunctionParse(MarkupText.Plain("rand(-5)")))!.Message!.ToPlainText());
		}

		await Assert.That(seen).IsNotEmpty();
		await Assert.That(seen.Count).IsGreaterThan(1);
		await Assert.That(seen.Except(["-4", "-3", "-2", "-1", "0"])).IsEmpty();
	}

	// Penn rand.5-rand.6 — deterministic two-arg rand cases
	[Test]
	[Arguments("rand(0,0)", "0")]
	[Arguments("rand(1,1)", "1")]
	public async Task RandDeterministic(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn randword.2 — randword of single word returns that word
	[Test]
	[Arguments("randword(foo)", "foo")]
	public async Task RandwordSingle(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("shuffle(a b c d e)", "")]
	public async Task Shuffle(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("scramble(test)", "")]
	public async Task Scramble(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
