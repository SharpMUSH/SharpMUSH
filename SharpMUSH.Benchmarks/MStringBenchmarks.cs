using SharpMUSH.MarkupString.TextAlignerModule;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Pure CPU benchmarks for the F#-backed <c>MString</c> / <c>MModule</c> markup-string library.
/// No database or DI container is required — these are allocation and throughput measurements.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
[BenchmarkCategory("Markup String")]
public class MStringBenchmarks
{
	// ── Pre-computed plain strings ─────────────────────────────────────────────
	private static readonly string Plain10 = new string('x', 10);
	private static readonly string Plain100 = new string('x', 100);
	private static readonly string Plain1000 = new string('x', 1000);

	// A string with embedded ANSI escape codes (green text, reset)
	private static readonly string AnsiStr50 =
		$"\x1b[32m{new string('g', 25)}\x1b[0m{new string('w', 25)}";

	// ── Pre-computed MStrings ──────────────────────────────────────────────────
	private static readonly MString Ms10 = MModule.single(Plain10);
	private static readonly MString Ms100 = MModule.single(Plain100);
	private static readonly MString Ms1000 = MModule.single(Plain1000);
	private static readonly MString MsAnsi = MModule.single(AnsiStr50);

	// Pre-computed column-alignment inputs
	private static readonly MString ColA = MModule.single("Name");
	private static readonly MString ColB = MModule.single("Score");
	private static readonly MString ColC = MModule.single("Rank");
	private static readonly MString Filler = MModule.single(" ");
	private static readonly MString ColSep = MModule.single(" ");
	private static readonly MString RowSep = MModule.single("\n");

	// ── single() — baseline construction ──────────────────────────────────────

	[Benchmark(Description = "MModule.single — 10 chars")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public MString CreateFromPlain(int length)
	{
		var str = length switch { 10 => Plain10, 100 => Plain100, _ => Plain1000 };
		return MModule.single(str);
	}

	[Benchmark(Description = "MModule.single — ANSI escape string (50 chars)")]
	public MString CreateFromAnsi() => MModule.single(AnsiStr50);

	// ── concat() ───────────────────────────────────────────────────────────────

	[Benchmark(Description = "MModule.concat — two 100-char plain strings")]
	public MString ConcatTwoStrings() => MModule.concat(Ms100, Ms100);

	[Benchmark(Description = "MModule.concatAttach — two strings (preserves ANSI)")]
	public MString ConcatAttach() => MModule.concatAttach(MsAnsi, Ms100);

	// ── getLength() ────────────────────────────────────────────────────────────

	[Benchmark(Description = "MModule.getLength — 100-char plain")]
	public int GetLengthPlain() => MModule.getLength(Ms100);

	[Benchmark(Description = "MModule.getLength — ANSI string (logical width)")]
	public int GetLengthAnsi() => MModule.getLength(MsAnsi);

	// ── substring() ───────────────────────────────────────────────────────────

	[Benchmark(Description = "MModule.substring — mid-section of 1000-char string")]
	public MString Substring() => MModule.substring(100, 50, Ms1000);

	// ── TextAlignerModule.align() ──────────────────────────────────────────────

	[Benchmark(Description = "TextAlignerModule.align — 3 columns 20/20/20")]
	public MString Align3Columns() =>
		TextAlignerModule.align("20 20 20", [ColA, ColB, ColC], Filler, ColSep, RowSep);

	[Benchmark(Description = "TextAlignerModule.align — 3 columns with multi-line content")]
	public MString AlignMultiLine()
	{
		var content = MModule.single("line1\nline2\nline3");
		return TextAlignerModule.align("30", [content], Filler, ColSep, RowSep);
	}

	// ── ToPlainText (rendering) ────────────────────────────────────────────────

	[Benchmark(Description = "MString.ToPlainText — plain 1000-char string")]
	public string ToPlainTextPlain() => Ms1000.ToPlainText();

	[Benchmark(Description = "MString.ToPlainText — ANSI string (strip escapes)")]
	public string ToPlainTextAnsi() => MsAnsi.ToPlainText();
}
