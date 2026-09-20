using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Pure CPU benchmarks for the <c>MarkupText</c> markup-string library: construction, editing,
/// rendering and serialisation. No database or DI container is required — these are allocation and
/// throughput measurements.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
[BenchmarkCategory("Markup String")]
public class MStringBenchmarks
{
	private static readonly string Plain10 = new('x', 10);
	private static readonly string Plain100 = new('x', 100);
	private static readonly string Plain1000 = new('x', 1000);

	private static readonly string AnsiStr50 =
		$"\x1b[32m{new string('g', 25)}\x1b[0m{new string('w', 25)}";

	private static readonly AnsiMarkup Green = AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false));
	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, true));

	private static readonly MarkupText Ms100 = MarkupText.Plain(Plain100);
	private static readonly MarkupText Ms1000 = MarkupText.Plain(Plain1000);
	private static readonly MarkupText MsAnsi = MarkupText.Wrap(Green, Plain100);

	/// <summary>One styled run — the shape almost every notification takes.</summary>
	private static readonly MarkupText OneRun = MarkupText.Wrap(Green, "The quick brown fox");

	/// <summary>
	/// 200 runs alternating between two styles: the pathological case for a renderer that restarts
	/// the escape state per run instead of diffing it.
	/// </summary>
	private static readonly MarkupText ManyRuns = MarkupText.Concat(
		Enumerable.Range(0, 200).Select(i => MarkupText.Wrap(i % 2 == 0 ? Green : Red, "word ")));

	private static readonly string OneRunJson = MarkupTextSerializer.Serialize(OneRun, Registry);

	private static readonly MarkupText ColA = MarkupText.Plain("Name");
	private static readonly MarkupText ColB = MarkupText.Plain("Score");
	private static readonly MarkupText ColC = MarkupText.Plain("Rank");
	private static readonly MarkupText Filler = MarkupText.Space;
	private static readonly MarkupText ColSep = MarkupText.Space;
	private static readonly MarkupText RowSep = MarkupText.NewLine;

	private static MarkupRegistry Registry => MarkupRegistry.Empty.WithAnsi().WithHtml();

	/// <summary>
	/// Rendering and serialisation resolve their emitters through <see cref="MarkupRegistry.Default"/>,
	/// whose getter throws until something assigns it.
	/// </summary>
	[GlobalSetup]
	public void ConfigureMarkup()
	{
		if (!MarkupRegistry.IsConfigured)
		{
			MarkupRegistry.Default = Registry;
		}
	}

	[Benchmark(Description = "MarkupText.Plain — 10/100/1000 chars")]
	[Arguments(10)]
	[Arguments(100)]
	[Arguments(1000)]
	public MarkupText CreateFromPlain(int length)
	{
		var str = length switch { 10 => Plain10, 100 => Plain100, _ => Plain1000 };
		return MarkupText.Plain(str);
	}

	[Benchmark(Description = "AnsiEscapeParser.Parse — ANSI escape string (50 chars)")]
	public MarkupText CreateFromAnsi() => AnsiEscapeParser.Parse(AnsiStr50);

	[Benchmark(Description = "MarkupText.Plain — allocation floor for a short string")]
	public MarkupText PlainAlloc() => MarkupText.Plain("hello");

	[Benchmark(Description = "MarkupText.Concat — two 100-char plain strings")]
	public MarkupText ConcatTwoStrings() => MarkupText.Concat(Ms100, Ms100);

	[Benchmark(Description = "MarkupText.AttachTail — extends the trailing run's markup")]
	public MarkupText ConcatAttach() => MsAnsi.AttachTail(Ms100);

	[Benchmark(Description = "MarkupText.Length — 100-char plain")]
	public int GetLengthPlain() => Ms100.Length;

	[Benchmark(Description = "MarkupText.DisplayWidth — styled 100-char string")]
	public int GetDisplayWidthAnsi() => MarkupText.Wrap(Green, Plain100).DisplayWidth;

	[Benchmark(Description = "MarkupText.Substring — mid-section of 1000-char string")]
	public MarkupText Substring() => Ms1000.Substring(100, 50);

	[Benchmark(Description = "TextAligner.Align — 3 columns 20/20/20")]
	public MarkupText Align3Columns() =>
		TextAligner.Align("20 20 20", [ColA, ColB, ColC], Filler, ColSep, RowSep);

	[Benchmark(Description = "TextAligner.Align — one column with multi-line content")]
	public MarkupText AlignMultiLine()
	{
		var content = MarkupText.Plain("line1\nline2\nline3");
		return TextAligner.Align("30", [content], Filler, ColSep, RowSep);
	}

	[Benchmark(Description = "MarkupText.ToPlainText — plain 1000-char string")]
	public string ToPlainTextPlain() => Ms1000.ToPlainText();

	[Benchmark(Description = "MarkupText.ToPlainText — styled string")]
	public string ToPlainTextAnsi() => MsAnsi.ToPlainText();

	[Benchmark(Description = "Render(Ansi) — one styled run")]
	public string RenderAnsiOneRun() => OneRun.Render(MarkupFormat.Ansi);

	[Benchmark(Description = "Render(Html) — one styled run")]
	public string RenderHtmlOneRun() => OneRun.Render(MarkupFormat.Html);

	[Benchmark(Description = "Render(Ansi) — 200 alternating runs")]
	public string Render200AlternatingRuns() => ManyRuns.Render(MarkupFormat.Ansi);

	[Benchmark(Description = "MarkupTextSerializer.Serialize — one styled run")]
	public string SerializeOneRun() => MarkupTextSerializer.Serialize(OneRun);

	[Benchmark(Description = "MarkupTextSerializer.Deserialize — one styled run")]
	public MarkupText DeserializeOneRun() => MarkupTextSerializer.Deserialize(OneRunJson);
}
