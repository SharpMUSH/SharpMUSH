using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The cost of <c>paren_groups</c>. With the option on, an unescaped <c>(</c> that starts no call opens a
/// literal group, as in PennMUSH; with it off, the same text is written with its literal parentheses
/// escaped. Each workload is that pair, one form per setting, doing the same work and producing the
/// same output, so the two columns compare like with like.
/// </summary>
/// <remarks>
/// <see cref="VerifyWorkloadsAsync"/> runs before anything is timed and fails the run if a pair ever
/// stops agreeing: an unescaped form read with the option off mis-splits its call, and timing that
/// would compare a parse error with a real evaluation. <c>paren-none</c> and <c>paren-escaped</c> are
/// the same text in both columns, which isolates what the counting itself costs.
/// </remarks>
[BenchmarkCategory("Parser")]
public class ParenGroupBenchmarks : LightningBaseBenchmark
{
	/// <summary>One workload: the text as PennMUSH writes it, and the same text escaped for the default.</summary>
	internal sealed record Workload(string Name, string Unescaped, string Escaped)
	{
		public string For(bool parenGroups) => parenGroups ? Unescaped : Escaped;
	}

	private static string Repeat(string element, int count, string separator = " ")
		=> string.Join(separator, Enumerable.Repeat(element, count));

	private static Workload Same(string name, string text) => new(name, text, text);

	internal static readonly Workload[] Workloads =
	[
		// A long evaluation with calls and prose, and not a parenthesis outside a call.
		Same("paren-none",
			$"[iter(lnum(20),[switch(%i0,>10,big [add(%i0,1)],small [mul(%i0,2)])])] {Repeat("plain words between the calls", 20)}"),
		// Literal parentheses written escaped, which reads the same with the option on or off.
		Same("paren-escaped", $"[cat({Repeat(@"\(note\) %(aside%) x", 20, ",")})]"),
		// Balanced groups inside a function's arguments.
		new("paren-args",
			$"[cat({Repeat("(a) b (c d) e", 20, ",")})]",
			$"[cat({Repeat("%(a%) b %(c d%) e", 20, ",")})]"),
		// Groups with commas in them, which the option keeps from splitting the call.
		new("paren-commas",
			$"[strlen({Repeat("(a,b,c) d", 40)})]",
			$"[strlen({Repeat("%(a%,b%,c%) d", 40)})]"),
		// Groups nested inside each other, inside a call.
		new("paren-nested",
			$"[strlen({new string('(', 200)}x{new string(')', 200)})]",
			$"[strlen({Repeat("%(", 200, "")}x{Repeat("%)", 200, "")})]"),
		// A name in text position followed by its own parenthesis, as prose about functions reads.
		new("paren-name",
			$"[cat({Repeat("see add(1,2) and mul(3,4) here", 10, ",")})]",
			$"[cat({Repeat("see add%(1%,2%) and mul%(3%,4%) here", 10, ",")})]"),
		// The regexp functions, whose patterns start with groups.
		new("paren-regexp",
			Repeat("[reswitch(abc,(a)(b)c,$1$2,(x)(y),$2,none)][regmatch(abc,(a)(b)c)][regedit(abc,(b)(c),$2$1)]", 10, ""),
			Repeat("[reswitch(abc,%(a%)%(b%)c,$1$2,%(x%)%(y%),$2,none)][regmatch(abc,%(a%)%(b%)c)][regedit(abc,%(b%)%(c%),$2$1)]", 10, "")),
		// A realistic attribute body: switches, iteration, regexps, and parentheses in the text it prints.
		new("paren-mixed",
			"[iter(lnum(10),[switch(%i0,>5,item %i0 (large),item %i0 (small))] [if(mod(%i0,2),(odd),(even))])]"
			+ " [reswitch(north gate,(north|south) (.+),you head $1 to the $2,nowhere)]"
			+ " [regeditall(the (big) cat,b(i)g,[ucstr($1)])]",
			"[iter(lnum(10),[switch(%i0,>5,item %i0 %(large%),item %i0 %(small%))] [if(mod(%i0,2),%(odd%),%(even%))])]"
			+ " [reswitch(north gate,%(north|south%) %(.+%),you head $1 to the $2,nowhere)]"
			+ " [regeditall(the %(big%) cat,b%(i%)g,[ucstr($1)])]"),
	];

	[Params(false, true)]
	public bool ParenGroups { get; set; }

	private IMUSHCodeParser? _parser;
	private DBRef _executor;
	private Dictionary<string, MString> _inputs = [];

	public override async ValueTask Setup()
	{
		await base.Setup();
		var parser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
		_executor = await BenchmarkHelpers.ExecutorDbRef(_database!);
		await VerifyWorkloadsAsync(parser, _executor);
		_parser = WithParenGroups(parser, ParenGroups);
		_inputs = Workloads.ToDictionary(workload => workload.Name, workload => MarkupText.Plain(workload.For(ParenGroups)));
	}

	/// <summary><paramref name="parser"/> reading its code with <c>paren_groups</c> set to <paramref name="on"/>.</summary>
	internal static IMUSHCodeParser WithParenGroups(IMUSHCodeParser parser, bool on)
	{
		var configured = (MUSHCodeParser)parser;
		var options = configured.Configuration.CurrentValue;
		return configured with
		{
			Configuration = new FixedOptions(options with
			{
				Compatibility = options.Compatibility with { ParenGroups = on }
			})
		};
	}

	/// <summary>
	/// Throws unless every workload's unescaped form, with the option on, evaluates to the same text as
	/// its escaped form with the option off.
	/// </summary>
	internal static async Task VerifyWorkloadsAsync(IMUSHCodeParser parser, DBRef executor)
	{
		var on = WithParenGroups(parser, true);
		var off = WithParenGroups(parser, false);
		foreach (var workload in Workloads)
		{
			var unescaped = await Evaluate(on, executor, workload.Unescaped);
			var escaped = await Evaluate(off, executor, workload.Escaped);
			if (unescaped != escaped)
			{
				throw new InvalidOperationException(
					$"{workload.Name}: paren_groups on gives '{unescaped}', off gives '{escaped}'; the two columns would not do the same work.");
			}
		}
	}

	private static async Task<string> Evaluate(IMUSHCodeParser parser, DBRef executor, string code)
		=> (await parser.FromState(BenchmarkHelpers.FreshState(executor)).FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText()
			?? string.Empty;

	private Task Run(string name) => _parser!.FromState(BenchmarkHelpers.FreshState(_executor)).FunctionParse(_inputs[name]).AsTask();

	[Benchmark(Baseline = true, Description = "no parentheses outside calls")]
	public Task NoParentheses() => Run("paren-none");

	[Benchmark(Description = "escaped literal parentheses")]
	public Task Escaped() => Run("paren-escaped");

	[Benchmark(Description = "balanced groups in arguments")]
	public Task GroupsInArguments() => Run("paren-args");

	[Benchmark(Description = "commas inside groups")]
	public Task CommasInGroups() => Run("paren-commas");

	[Benchmark(Description = "200 nested groups")]
	public Task NestedGroups() => Run("paren-nested");

	[Benchmark(Description = "name( in text position")]
	public Task NameThenParen() => Run("paren-name");

	[Benchmark(Description = "regexp patterns starting with groups")]
	public Task RegexpPatterns() => Run("paren-regexp");

	[Benchmark(Description = "mixed attribute body")]
	public Task MixedAttribute() => Run("paren-mixed");

	private sealed class FixedOptions(SharpMUSHOptions value) : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue => value;
	}
}
