namespace SharpMUSH.Benchmarks;

/// <summary>
/// An attribute run as a function, once and once per list item, with a body under and over the length the
/// parse cache keys on text (<c>SoftcodeParseCache.MaxTextLength</c>). Attribute bodies are keyed on the
/// attribute and its modification time, so a long body is parsed once like a short one.
/// </summary>
[BenchmarkCategory("Parse Cache")]
public class AttributeParseCacheBenchmarks : LightningBaseBenchmark
{
	/// <summary>One unit of body: a few nested calls on the argument, as a formatting function would make.</summary>
	private const string Unit = "[add(%0,1)][mul(%0,2)][if(gt(%0,5),big,small)]";

	private MString? _shortCall;
	private MString? _longCall;
	private MString? _iterLongCall;

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);

		var one = await BenchmarkHelpers.ExecutorDbRef(_database!).ConfigureAwait(false);
		var god = await _database!.GetObjectNodeAsync(one).ConfigureAwait(false) is AnySharpObject and SharpPlayer player
			? player
			: throw new InvalidOperationException("God (#1) is not seeded as a player.");

		// About 470 and 6,100 characters: one a text key covers, one it does not.
		await _database.SetAttributeAsync(one, ["SHORT"], MarkupText.Plain(string.Concat(Enumerable.Repeat(Unit, 10))), god)
			.ConfigureAwait(false);
		await _database.SetAttributeAsync(one, ["LONG"], MarkupText.Plain(string.Concat(Enumerable.Repeat(Unit, 130))), god)
			.ConfigureAwait(false);

		_shortCall = MarkupText.Plain("u(#1/SHORT,3)");
		_longCall = MarkupText.Plain("u(#1/LONG,3)");
		_iterLongCall = MarkupText.Plain("iter(lnum(20),u(#1/LONG,%i0))");
	}

	[Benchmark(Description = "u() of a 470-character body")]
	public async Task ShortBody() => await FreshParser().FunctionParse(_shortCall!);

	[Benchmark(Description = "u() of a 6,100-character body")]
	public async Task LongBody() => await FreshParser().FunctionParse(_longCall!);

	[Benchmark(Description = "iter() of 20 u() calls to a 6,100-character body")]
	public async Task LongBodyPerItem() => await FreshParser().FunctionParse(_iterLongCall!);
}
