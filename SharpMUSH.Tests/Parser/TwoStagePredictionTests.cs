using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// The parser defaults to two-stage prediction: SLL first, re-running LL only when SLL reports a
/// syntax error. These exercise both stages through the public API. Valid input takes the SLL
/// path; malformed input (in strict function evaluation) forces the SLL bail and the LL re-parse
/// over the rewound token stream — if that rewind were wrong, the second pass would misreport or
/// throw rather than reproduce the syntax error. Across the whole suite, which now runs under this
/// default, the two stages agree on every case; these are the focused witnesses.
/// </summary>
public class TwoStagePredictionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// Valid expressions of varying shape — the SLL pass handles all of these with no fallback.
	[Test]
	[Arguments("add(1,2)", "3")]
	[Arguments("[add(1,2)]", "3")]
	[Arguments("strcat(a,b,c)", "abc")]
	[Arguments("iter(1 2 3,add(##,1))", "2 3 4")]
	[Arguments("switch(2,1,one,2,two,other)", "two")]
	[Arguments("[setr(0,hi)]%q0", "hihi")]
	public async Task ValidInputParsesCorrectly(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// A genuine syntax error in strict evaluation: SLL bails, the LL re-parse runs on the rewound
	// stream and reports the failure. The point is that the second pass produces a clean failure
	// string, proving the rewind rather than the specific wording.
	[Test]
	[Arguments("add(1,2")]
	[Arguments("strcat(strcat(dog)")]
	public async Task SyntaxErrorSurfacesAsFailureAfterFallback(string input)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#-1 PARSER FAILURE");
	}

	/// <summary>
	/// The token stream is rewound and re-walked between the two stages, and the DFA/ATN caches are
	/// process-wide statics; a leak in either would make a repeated parse of the same input drift.
	/// Parsing the same expressions many times must give a stable answer every time.
	/// </summary>
	[Test]
	[Arguments("iter(1 2 3 4 5,mul(##,2))", "2 4 6 8 10")]
	[Arguments("[add(1,2)][add(3,4)]", "37")]
	public async Task RepeatedParsesAreStable(string input, string expected)
	{
		for (var i = 0; i < 25; i++)
		{
			var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
			await Assert.That(result.ToPlainText()).IsEqualTo(expected);
		}
	}
}
