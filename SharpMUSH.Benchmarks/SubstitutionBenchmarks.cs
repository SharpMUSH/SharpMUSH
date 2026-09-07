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
	private static readonly MString ExecDbRefCmd = MarkupText.Plain("think %#");
	private static readonly MString ExecNameCmd = MarkupText.Plain("think %N");

	private static readonly MString Cat3Subst = MarkupText.Plain("[cat(%#,%#,%#)]");
	private static readonly MString Cat10Subst = MarkupText.Plain(
		$"[cat({string.Join(",", Enumerable.Repeat("%#", 10))})]");

	private static readonly MString SetQRead = MarkupText.Plain("[setq(0,hello)]%q0");

	private static readonly MString IterReg5 = MarkupText.Plain("iter(lnum(5),%i0)");
	private static readonly MString IterReg50 = MarkupText.Plain("iter(lnum(50),%i0)");

	private static readonly MString Add1Subst = MarkupText.Plain("[add(0,%#)]");
	private static readonly MString Add5Subst = MarkupText.Plain(
		"[add(%#,[add(%#,[add(%#,[add(%#,%#)])])])]");


	[Benchmark(Description = "think %# — executor dbref command subst")]
	public async Task ThinkDbRef() =>
		await FreshParser().CommandParse(ExecDbRefCmd);

	[Benchmark(Description = "think %N — executor name command subst")]
	public async Task ThinkName() =>
		await FreshParser().CommandParse(ExecNameCmd);

	[Benchmark(Description = "[cat(%#,…)] — 3 substitutions in function")]
	public async Task Cat3Substitutions() =>
		await FreshParser().FunctionParse(Cat3Subst);

	[Benchmark(Description = "[cat(%#,…)] — 10 substitutions in function")]
	public async Task Cat10Substitutions() =>
		await FreshParser().FunctionParse(Cat10Subst);

	[Benchmark(Description = "[setq(0,hello)]%q0 — q-register set+read")]
	public async Task SetQRegisterAndRead() =>
		await FreshParser().FunctionParse(SetQRead);

	[Benchmark(Description = "iter(lnum(5),%i0) — iteration register access")]
	public async Task IterRegisterSmall() =>
		await FreshParser().FunctionParse(IterReg5);

	[Benchmark(Description = "iter(lnum(50),%i0) — iteration register access at scale")]
	public async Task IterRegisterMedium() =>
		await FreshParser().FunctionParse(IterReg50);

	[Benchmark(Description = "[add(0,%#)] — single nested substitution in function")]
	public async Task AddSingleSubstitution() =>
		await FreshParser().FunctionParse(Add1Subst);

	[Benchmark(Description = "nested add(%#,…) x5 — 5-deep nested substitution")]
	public async Task AddDeepSubstitution() =>
		await FreshParser().FunctionParse(Add5Subst);
}
