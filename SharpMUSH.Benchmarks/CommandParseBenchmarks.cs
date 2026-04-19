namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for MUSH command dispatch via <see cref="IMUSHCodeParser.CommandParse(MString)"/>.
/// Covers the hot path from raw input → ANTLR4 parse → command lookup (trie) → command execution.
/// </summary>
[BenchmarkCategory("Command Dispatch")]
public class CommandParseBenchmarks : BaseBenchmark
{
	private IMUSHCodeParser? _parser;

	// Pre-computed inputs to eliminate per-iteration allocation overhead.
	private static readonly MString ThinkSimpleInput = MModule.single("think Hello World");
	private static readonly MString ThinkSubstInput = MModule.single("think %#");
	private static readonly MString ThinkNameSubstInput = MModule.single("think %N");
	private static readonly MString PemitSelfInput = MModule.single("@pemit me=Hello World");
	private static readonly MString SetAttrInput = MModule.single("@set me=SAFE");

	public override async ValueTask Setup()
	{
		await base.Setup().ConfigureAwait(false);
		_parser = await TestParser().ConfigureAwait(false);
	}

	[Benchmark(Description = "think with literal text")]
	public async Task ThinkSimple() =>
		await _parser!.CommandParse(ThinkSimpleInput);

	[Benchmark(Description = "think with %# (executor dbref)")]
	public async Task ThinkWithDbRefSubstitution() =>
		await _parser!.CommandParse(ThinkSubstInput);

	[Benchmark(Description = "think with %N (executor name)")]
	public async Task ThinkWithNameSubstitution() =>
		await _parser!.CommandParse(ThinkNameSubstInput);

	[Benchmark(Description = "@pemit me=Hello World")]
	public async Task PemitSelf() =>
		await _parser!.CommandParse(PemitSelfInput);

	[Benchmark(Description = "@set me=SAFE (flag toggle)")]
	public async Task SetFlag() =>
		await _parser!.CommandParse(SetAttrInput);
}
