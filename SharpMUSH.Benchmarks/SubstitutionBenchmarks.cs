namespace SharpMUSH.Benchmarks;

/// <summary>
/// Benchmarks for the MUSH substitution and register system.
/// Tests the <c>%#</c>, <c>%N</c>, <c>%q</c>, and <c>%i</c> expansion hot paths
/// inside both <see cref="IMUSHCodeParser.FunctionParse(MString)"/> and
/// <see cref="IMUSHCodeParser.CommandParse(MString)"/>.
/// </summary>
[BenchmarkCategory("Substitution & Registers")]
public class SubstitutionBenchmarks : BaseBenchmark
{
	private IMUSHCodeParser? _parser;

	// %# / %N — always available, no register setup required
	private static readonly MString ExecDbRefCmd = MModule.single("think %#");
	private static readonly MString ExecNameCmd = MModule.single("think %N");

	// cat() with multiple %# to stress repeated substitution expansion
	private static readonly MString Cat3Subst = MModule.single("[cat(%#,%#,%#)]");
	private static readonly MString Cat10Subst = MModule.single(
		$"[cat({string.Join(",", Enumerable.Repeat("%#", 10))})]");

	// setq + read — exercises the q-register write+read path
	private static readonly MString SetQRead = MModule.single("[setq(0,hello)]%q0");

	// iter — exercises iteration register (%i0) population and access
	private static readonly MString IterReg5 = MModule.single("iter(lnum(5),%i0)");
	private static readonly MString IterReg50 = MModule.single("iter(lnum(50),%i0)");

	// Nested substitutions: add() with %# embedded at various recursion depths
	private static readonly MString Add1Subst = MModule.single("[add(0,%#)]");
	private static readonly MString Add5Subst = MModule.single(
		"[add(%#,[add(%#,[add(%#,[add(%#,%#)])])])]");

	public SubstitutionBenchmarks()
	{
		Setup().ConfigureAwait(false).GetAwaiter().GetResult();
		_parser = TestParser().ConfigureAwait(false).GetAwaiter().GetResult();
	}

	[Benchmark(Description = "think %# — executor dbref command subst")]
	public async Task ThinkDbRef() =>
		await _parser!.CommandParse(ExecDbRefCmd);

	[Benchmark(Description = "think %N — executor name command subst")]
	public async Task ThinkName() =>
		await _parser!.CommandParse(ExecNameCmd);

	[Benchmark(Description = "[cat(%#,…)] — 3 substitutions in function")]
	public async Task Cat3Substitutions() =>
		await _parser!.FunctionParse(Cat3Subst);

	[Benchmark(Description = "[cat(%#,…)] — 10 substitutions in function")]
	public async Task Cat10Substitutions() =>
		await _parser!.FunctionParse(Cat10Subst);

	[Benchmark(Description = "[setq(0,hello)]%q0 — q-register set+read")]
	public async Task SetQRegisterAndRead() =>
		await _parser!.FunctionParse(SetQRead);

	[Benchmark(Description = "iter(lnum(5),%i0) — iteration register access")]
	public async Task IterRegisterSmall() =>
		await _parser!.FunctionParse(IterReg5);

	[Benchmark(Description = "iter(lnum(50),%i0) — iteration register access at scale")]
	public async Task IterRegisterMedium() =>
		await _parser!.FunctionParse(IterReg50);

	[Benchmark(Description = "[add(0,%#)] — single nested substitution in function")]
	public async Task AddSingleSubstitution() =>
		await _parser!.FunctionParse(Add1Subst);

	[Benchmark(Description = "nested add(%#,…) x5 — 5-deep nested substitution")]
	public async Task AddDeepSubstitution() =>
		await _parser!.FunctionParse(Add5Subst);
}
