using System.Text.RegularExpressions;
using MarkupString.Ansi;
using MarkupString.Layout;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Every gradient() example in its help, drawn for a client at each colour depth: a truecolour or a
/// 256-colour client sees the colours change along the text, and a sixteen-colour client the bands
/// of the stops' own colours that the help says it gets.
/// </summary>
public partial class GradientHelpExampleTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<MString> Eval(string code) =>
		await FunctionParser.EvaluateAsync(MarkupText.Plain(code));

	/// <summary>Each <c>&gt; think gradient(...)</c> line of the gradient() help, without the <c>think</c>.</summary>
	public static IEnumerable<Func<string>> Examples() =>
		File.ReadAllLines(Path.Join(TestPaths.Helpfiles.FullName, "gradient-function.md"))
			.Where(line => line.StartsWith("> think gradient(", StringComparison.Ordinal))
			.Select(line => (Func<string>)(() => line["> think ".Length..]));

	[Test]
	public async Task TheHelpHasAnExampleForEachFlowAndOption()
	{
		var examples = string.Join("\n", Examples().Select(example => example()));

		foreach (var option in new[] { "\"flow\":\"words\"", "\"flow\":\"across\"", "\"flow\":\"down\"", "\"flow\":\"diagonal\"", "\"space\":", "\"mirror\":true", "\"repeat\":", "\"target\":\"borders\"", "\"target\":\"text\"", ",primary|info" })
			await Assert.That(examples).Contains(option);
		foreach (var layout in new[] { "gradient(box(", "gradient(datatable(" })
			await Assert.That(examples).Contains(layout);
	}

	[Test]
	[MethodDataSource(nameof(Examples))]
	public async Task AtTruecolour_TheColoursChangeAlongTheText(string code)
		=> await Assert.That(Colours(Render(await Eval(code), AnsiColorDepth.TrueColor)).Distinct().Count()).IsGreaterThanOrEqualTo(4);

	[Test]
	[MethodDataSource(nameof(Examples))]
	public async Task At256Colours_TheColoursChangeAlongTheText(string code)
		=> await Assert.That(Colours(Render(await Eval(code), AnsiColorDepth.Xterm256)).Distinct().Count()).IsGreaterThanOrEqualTo(3);

	/// <summary>
	/// A sixteen-colour client gets each of the stops' own colours, and no other but those the text had
	/// before it was shaded (the theme's, on the part a <c>target</c> leaves out).
	/// </summary>
	[Test]
	[MethodDataSource(nameof(Examples))]
	public async Task AtSixteenColours_TheTextIsInBandsOfTheStops(string code)
	{
		var arguments = Arguments(code);
		var stops = new HashSet<string>();
		foreach (var stop in arguments[1].Split('|'))
			stops.UnionWith(Colours(Render(ToneMarkup.TryParse(stop, out _)
				? ToneMarkup.ForTelnet(await Eval($"tone({stop},x)"), null)
				: await Eval($"ansi({stop},x)"), AnsiColorDepth.Standard)));

		var before = Colours(Render(await Eval(arguments[0]), AnsiColorDepth.Standard)).ToHashSet();
		var shown = Colours(Render(await Eval(code), AnsiColorDepth.Standard)).ToHashSet();

		await Assert.That(shown.Except(before).ToHashSet().IsSubsetOf(stops)).IsTrue();
		await Assert.That(stops.IsSubsetOf(shown)).IsTrue();
	}

	/// <summary>
	/// A gradient over a layout shades it borders and all, in the game's layout_theme and in a reader's
	/// own @theme alike: the colours a theme gives a box's border and title do not win over it.
	/// </summary>
	[Test]
	public async Task OverAThemedBox_TheBorderAndTitleAreShadedToo()
	{
		if (!ThemePalette.TryParse("fantasy", out var fantasy, out _)) throw new InvalidOperationException("fantasy does not read");
		var box = await Eval("gradient(box(Sheet,Mannaz,20),#ff0000|#0000ff,{{\"flow\":\"diagonal\"}})");

		foreach (var text in new[] { box, BlockLayout.Relayout(box, 20, new LayoutContext { Theme = fantasy!.ToLayoutTheme() }) })
		{
			var lines = Render(text, AnsiColorDepth.TrueColor).Split('\n');
			await Assert.That(lines[0]).StartsWith("\u001b[38;2;255;0;0m");
			await Assert.That(Colours(lines[0]).Distinct().Count()).IsGreaterThanOrEqualTo(4);
			await Assert.That(lines[^1]).Contains("\u001b[38;2;0;0;255m");
		}
	}

	private static string Render(MString text, AnsiColorDepth depth) =>
		text.Render(MarkupFormat.Ansi, MarkupRegistry.Default.WithAnsiOutput(depth, hyperlinks: false));

	/// <summary>The arguments of a <c>gradient(...)</c> call, split at its own commas.</summary>
	private static string[] Arguments(string code)
	{
		var commas = new List<int>();
		var depth = 0;
		for (var i = "gradient(".Length; i < code.Length; i++)
		{
			if (code[i] is '(' or '{' or '[') depth++;
			else if (code[i] is ')' or '}' or ']') depth--;
			else if (code[i] == ',' && depth == 0) commas.Add(i);
		}
		var bounds = commas.Prepend("gradient(".Length - 1).Append(code.Length - 1).ToArray();
		return [.. bounds.Zip(bounds.Skip(1), (start, end) => code[(start + 1)..end])];
	}

	/// <summary>The foreground colour each character that shows is written in, as its SGR parameters (<c>38;2;r;g;b</c>, <c>35</c>).</summary>
	private static IEnumerable<string> Colours(string ansi)
	{
		string? colour = null;
		var at = 0;
		foreach (Match sgr in Sgr().Matches(ansi))
		{
			foreach (var character in ansi[at..sgr.Index])
				if (!char.IsWhiteSpace(character) && colour is not null) yield return colour;
			at = sgr.Index + sgr.Length;
			colour = Foreground(sgr.Groups[1].Value.Split(';'), colour);
		}
		foreach (var character in ansi[at..])
			if (!char.IsWhiteSpace(character) && colour is not null) yield return colour;
	}

	private static string? Foreground(string[] parameters, string? colour)
	{
		for (var i = 0; i < parameters.Length; i++)
		{
			switch (parameters[i])
			{
				case "" or "0" or "39":
					colour = null;
					break;
				case "38" when i + 1 < parameters.Length && parameters[i + 1] == "2":
					colour = string.Join(';', parameters[i..(i + 5)]);
					i += 4;
					break;
				case "38" when i + 1 < parameters.Length && parameters[i + 1] == "5":
					colour = string.Join(';', parameters[i..(i + 3)]);
					i += 2;
					break;
				case [('3' or '9'), >= '0' and <= '7']:
					colour = parameters[i];
					break;
			}
		}
		return colour;
	}

	[GeneratedRegex(@"\u001b\[([0-9;]*)m")]
	private static partial Regex Sgr();
}
