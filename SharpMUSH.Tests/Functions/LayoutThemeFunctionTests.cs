using MarkupString.Layout;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>The <c>theme</c> layout option, <c>themes()</c>, <c>theme()</c> and <c>swatch()</c>.</summary>
public class LayoutThemeFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<MString> Eval(string code) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(code)))!.Message!;

	/// <summary>Nord's primary, #81a1c1, as a truecolour foreground.</summary>
	private const string NordBlue = "38;2;129;161;193m";

	[Test]
	public async Task ATheme_ColoursTheBorders()
	{
		var themed = await Eval("box(Hi,T,12,{{\"theme\":\"nord\"}})");
		var plain = await Eval("box(Hi,T,12)");

		await Assert.That(themed.Render(MarkupFormat.Ansi)).Contains(NordBlue);
		await Assert.That(themed.ToPlainText()).IsEqualTo(plain.ToPlainText());
		await Assert.That(plain.Render(MarkupFormat.Ansi)).DoesNotContain(NordBlue);
	}

	[Test]
	public async Task ATheme_IsANameOrJsonWrittenInPlace()
	{
		var nested = await Eval("box(Hi,,12,{{\"theme\":{\"preset\":\"nord\",\"colors\":{\"primary\":\"#ff0000\"}}}})");
		var quoted = await Eval("box(Hi,,12,{{\"theme\":\"{\\\\\"seed\\\\\":\\\\\"#ff0000\\\\\",\\\\\"harmony\\\\\":\\\\\"triadic\\\\\"}\"}})");

		await Assert.That(nested.Render(MarkupFormat.Ansi)).Contains("38;2;255;0;0m");
		await Assert.That(quoted.Render(MarkupFormat.Ansi)).Contains("38;2;");
	}

	[Test]
	public async Task ATheme_ReachesTheLayoutsInside()
	{
		var result = await Eval("flex({{\"theme\":\"nord\",\"width\":30}},box(Left,,14),box(Right,,14))");

		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains(NordBlue);
	}

	[Test]
	public async Task TheTerminalTheme_SendsStandardColours()
	{
		var result = await Eval("box(Hi,,12,{{\"theme\":\"terminal\"}})");

		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[36m");
	}

	[Test]
	[Arguments("box(Hi,,12,{{\"theme\":\"nowhere\"}})", "#-1 UNKNOWN THEME")]
	[Arguments("box(Hi,,12,{{\"theme\":{\"seed\":\"blue\"}}})", "#-1 INVALID THEME: seed is a colour like #7aa2f7")]
	[Arguments("box(Hi,,12,{{\"pad\":{\"x\":1}}})", "#-1 INVALID ARGUMENT")]
	[Arguments("theme(nowhere)", "#-1 UNKNOWN THEME")]
	[Arguments("swatch({{\"mode\":\"grey\"}})", "#-1 INVALID THEME: mode is dark or light")]
	public async Task ABadTheme_SaysWhy(string code, string error)
		=> await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(error);

	[Test]
	public async Task Themes_ListsThePresets()
		=> await Assert.That((await Eval("themes()")).ToPlainText())
			.IsEqualTo("terminal catppuccin-mocha catppuccin-latte dracula gruvbox-dark nord solarized-dark solarized-light tokyo-night");

	[Test]
	public async Task Theme_WritesTheWholePaletteOut()
	{
		var json = (await Eval("theme({{\"seed\":\"#d08770\",\"harmony\":\"split\"}})")).ToPlainText();

		await Assert.That(json).StartsWith("{\"name\":\"generated\",\"mode\":\"dark\",\"colors\":{\"background\":{\"rgb\":\"#");
		await Assert.That((await Eval("theme({" + json + "})")).ToPlainText()).IsEqualTo(json);
		await Assert.That((await Eval("json_query(theme(nord),type)")).ToPlainText()).IsEqualTo("object");
	}

	[Test]
	public async Task Swatch_ShowsEachRole()
	{
		var swatch = await Eval("swatch(nord,78)");
		var text = swatch.ToPlainText();

		await Assert.That(text).Contains("primary");
		await Assert.That(text).Contains("#81a1c1");
		await Assert.That(text).Contains("4 blue");
		await Assert.That(swatch.Render(MarkupFormat.Ansi)).Contains(NordBlue + "Sample");
	}

	[Test]
	public async Task Swatch_MarksAColourTooFaint()
		=> await Assert.That((await Eval("swatch({{\"preset\":\"nord\",\"colors\":{\"primary\":\"#3b4252\"}}},78)")).ToPlainText())
			.Contains("(needs 3)");

	[Test]
	public async Task AThemedLayout_StaysThemedForEachReader()
	{
		var result = await Eval("box(Hi,,,{{\"theme\":\"nord\"}})");
		var ascii = BlockLayout.Relayout(result, 40, new LayoutContext { AsciiOnly = true });

		await Assert.That(ascii.Render(MarkupFormat.Ansi)).Contains(NordBlue + "+");
	}
}
