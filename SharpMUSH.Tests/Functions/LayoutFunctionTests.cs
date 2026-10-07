using MarkupString.Layout;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>box()</c>, <c>rule()</c>, <c>flex()</c>, <c>item()</c> and <c>figure()</c> return the box art a
/// terminal shows, with the layout it was drawn from riding on it for the portal.
/// </summary>
public class LayoutFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<MString> Eval(string code) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(code)))!.Message!;

	private static string Lines(params string[] lines) => string.Join("\n", lines);

	private static string TrimLines(MString text) =>
		string.Join("\n", text.ToPlainText().Split('\n').Select(line => line.TrimEnd()));

	[Test]
	public async Task Box_DrawsAMushFingerBox()
	{
		var result = await Eval("box(flex({{\"sep\":\" | \"}},item(Sex: Male%rSpecies: Human,35),item(Job: Dark Warrior%rOnline: 1h,36))%r[rule(Quote)]%rHooooo?,Mannaz Byron,78,{{\"open\":\"<< \",\"close\":\" >>\"}})");

		await Assert.That(result.ToPlainText()).IsEqualTo(Lines(
			"+=============================<< Mannaz Byron >>=============================+",
			"| Sex: Male                           | Job: Dark Warrior                    |",
			"| Species: Human                      | Online: 1h                           |",
			"+=================================<< Quote >>================================+",
			"| Hooooo?                                                                    |",
			"+============================================================================+"));
	}

	/// <summary>The portal draws the same layout as page structure, not as box art.</summary>
	[Test]
	public async Task Box_IsACardInTheBrowser()
	{
		var html = (await Eval("box(flex({{\"sep\":\" | \"}},item(Left,20),item(Right,20))%r[rule(Quote)]%rHooooo?,Title,50)")).Render(MarkupFormat.Html);

		await Assert.That(html).StartsWith("<div class=\"ms-layout\" style=\"max-width:50ch\"><fieldset class=\"ms-box ms-border-mush\"><legend class=\"ms-box-title\">Title</legend>");
		await Assert.That(html).Contains("<div class=\"ms-flex ms-divided\"");
		await Assert.That(html).Contains("<div class=\"ms-divider ms-border-mush\" role=\"separator\"><span class=\"ms-rule-title\">Quote</span></div>");
		await Assert.That(html).DoesNotContain("+===");
	}

	[Test]
	public async Task Box_TakesAPresetAndItsOwnPieces()
	{
		await Assert.That((await Eval("box(Hello there.,,30,{{\"border\":\"rounded\"}})")).ToPlainText()).IsEqualTo(Lines(
			"╭────────────────────────────╮",
			"│ Hello there.               │",
			"╰────────────────────────────╯"));
		await Assert.That((await Eval("box(x,,20,{{\"top\":\"-=\",\"corner\":\"*\"}})")).ToPlainText()).IsEqualTo(Lines(
			"*-=-=-=-=-=-=-=-=-=*",
			"| x                |",
			"*==================*"));
	}

	[Test]
	public async Task Rule_SetsItsTitleIntoTheLine()
		=> await Assert.That((await Eval("rule(Factions,40)")).ToPlainText())
			.IsEqualTo("==============< Factions >==============");

	[Test]
	public async Task Flex_PutsItemsSideBySide()
		=> await Assert.That(TrimLines(await Eval("flex({{\"sep\":\" | \",\"width\":40}},item(Strength%rAgility,18),item(High%rLow,19))")))
			.IsEqualTo(Lines(
				"Strength           | High",
				"Agility            | Low"));

	[Test]
	public async Task Flex_StacksItemsThatDoNotFit()
		=> await Assert.That(TrimLines(await Eval("flex({{\"width\":20}},item(Left,15,15),item(Right,15,15))")))
			.IsEqualTo(Lines("Left", "Right"));

	[Test]
	public async Task Figure_FlowsTextBesideItsArt()
	{
		var result = await Eval("figure(https://example.com/cat.png,A cat,/|_/|%r=^.^=%r%b> <,left,The cat sits by the fire and watches the door all night long.,24)");

		await Assert.That(TrimLines(result)).IsEqualTo(Lines(
			"/|_/|  The cat sits by",
			"=^.^=  the fire and",
			" > <   watches the door",
			"all night long."));
		await Assert.That(result.Render(MarkupFormat.Html))
			.Contains("<img class=\"ms-figure-image\" src=\"https://example.com/cat.png\" alt=\"A cat\">");
	}

	/// <summary>Only an http, https or relative address is a picture; anything else leaves the art.</summary>
	[Test]
	public async Task Figure_RefusesAnAddressThatIsNotAPicture()
	{
		var html = (await Eval("figure(javascript:alert\\(1\\),A cat,=^.^=)")).Render(MarkupFormat.Html);

		await Assert.That(html).DoesNotContain("<img");
		await Assert.That(html).Contains("<pre class=\"ms-figure-art\" role=\"img\" aria-label=\"A cat\">=^.^=</pre>");
	}

	[Test]
	public async Task Fields_LineTheValuesUp()
		=> await Assert.That(TrimLines(await Eval("fields({{\"width\":40}},Sex,Male,Species,Human,Origin,Super Robot Wars AG)")))
			.IsEqualTo(Lines(
				"Sex:     Male",
				"Species: Human",
				"Origin:  Super Robot Wars AG"));

	[Test]
	public async Task Fields_TakeALeaderAndRightAlignedLabels()
	{
		await Assert.That(TrimLines(await Eval("fields({{\"width\":40,\"leader\":\".\"}},Sex,Male,Species,Human)")))
			.IsEqualTo(Lines("Sex....: Male", "Species: Human"));
		await Assert.That(TrimLines(await Eval("fields({{\"width\":40,\"align\":\"right\"}},Sex,Male,Species,Human)")))
			.IsEqualTo(Lines("    Sex: Male", "Species: Human"));
	}

	/// <summary>The finger box the layout functions were made for, with its key/value pairs as fields.</summary>
	[Test]
	public async Task Fields_InsideABox_MakeAFingerSheet()
	{
		var result = await Eval("box(fields({{\"cols\":2}},Sex,Male,Species,Human,Job,Dark Warrior,Online,1h)%r[rule(Quote)]%rHooooo?,Mannaz Byron,60)");

		await Assert.That(result.ToPlainText()).IsEqualTo(Lines(
			"+=====================< Mannaz Byron >=====================+",
			"| Sex:     Male                 Job:    Dark Warrior       |",
			"| Species: Human                Online: 1h                 |",
			"+=========================< Quote >========================+",
			"| Hooooo?                                                  |",
			"+==========================================================+"));
		await Assert.That(result.Render(MarkupFormat.Html)).Contains("<dl class=\"ms-fields\"><div class=\"ms-field\"><dt>Sex:</dt>");
	}

	[Test]
	public async Task Tree_DrawsNodesUnderTheirParents()
	{
		var result = await Eval("tree({{\"width\":30}},node(Channels,node(Public,+chat,+ooc),node(Staff,+admin)))");

		await Assert.That(TrimLines(result)).IsEqualTo(Lines(
			"Channels",
			"├─ Public",
			"│  ├─ +chat",
			"│  └─ +ooc",
			"└─ Staff",
			"   └─ +admin"));
		await Assert.That(result.Render(MarkupFormat.Html)).StartsWith("<div class=\"ms-layout\" style=\"max-width:30ch\"><ul class=\"ms-tree ms-guide-line\">");
	}

	[Test]
	public async Task Tree_TakesAGuideAndItsOwnPieces()
	{
		await Assert.That(TrimLines(await Eval("tree({{\"width\":30,\"guide\":\"ascii\"}},node(Mail,Inbox,Sent))")))
			.IsEqualTo(Lines("Mail", "|- Inbox", "`- Sent"));
		await Assert.That(TrimLines(await Eval("tree({{\"width\":30,\"branch\":\"+> \",\"last\":\"*> \"}},node(Mail,Inbox,Sent))")))
			.IsEqualTo(Lines("Mail", "+> Inbox", "*> Sent"));
	}

	[Test]
	public async Task Gradient_ColoursEachCharacterAlongTheWay_SkippingSpaces()
	{
		var ansi = (await Eval("gradient(a b,#ff0000|#0000ff)")).Render(MarkupFormat.Ansi);

		await Assert.That(ansi).Contains("\u001b[38;2;255;0;0ma");
		await Assert.That(ansi).Contains("\u001b[38;2;0;0;255mb");
	}

	[Test]
	public async Task Gauge_Gradient_IsDrawnInTheSameSpaceOnTheWeb()
	{
		var html = (await Eval("gauge(6,12,,{{\"bar\":4,\"gradient\":\"#ff0000|#00ff00\"}})")).Render(MarkupFormat.Html);

		await Assert.That(html).Contains("linear-gradient(to right in oklch, #ff0000, #00ff00)");
		await Assert.That(html).Contains("<div class=\"ms-gauge-fill\" style=\"width:50%;");
	}

	[Test]
	public async Task Gauge_ShadedByValue_IsOneColour()
		=> await Assert.That((await Eval("gauge(12,12,,{{\"bar\":2,\"show\":\"none\",\"open\":\"\",\"close\":\"\",\"shade\":\"value\",\"space\":\"hsl\",\"gradient\":\"#ff0000|#0000ff\"}})")).Render(MarkupFormat.Ansi))
			.Contains("\u001b[38;2;0;0;255m██");

	[Test]
	public async Task Gradient_FlowsDownTheLines_EachLineOneColour()
	{
		var ansi = (await Eval("gradient(ab%rcd,#ff0000|#0000ff,{{\"flow\":\"down\"}})")).Render(MarkupFormat.Ansi);

		await Assert.That(ansi).Contains("\u001b[38;2;255;0;0mab");
		await Assert.That(ansi).Contains("\u001b[38;2;0;0;255mcd");
	}

	[Test]
	public async Task Gradient_MirrorAndRepeat_RunTheColoursAgain()
	{
		var mirrored = (await Eval("gradient(abc,#ff0000|#0000ff,{{\"mirror\":true}})")).Render(MarkupFormat.Ansi);
		var repeated = (await Eval("gradient(abcde,#ff0000|#0000ff,{{\"repeat\":2}})")).Render(MarkupFormat.Ansi);

		await Assert.That(mirrored).Contains("\u001b[38;2;255;0;0mc");
		await Assert.That(repeated).Contains("\u001b[38;2;255;0;0mc");
	}

	[Test]
	public async Task Gradient_OverALayout_ShadesTheBlockAndKeepsItALayout()
	{
		var shaded = await Eval("gradient(box(Hi,T,12),#ff0000|#0000ff,{{\"flow\":\"diagonal\"}})");

		await Assert.That(BlockLayout.AsBlock(shaded)).IsTypeOf<Shaded>();
		await Assert.That(shaded.Render(MarkupFormat.Html)).Contains("ms-shaded");
		await Assert.That(shaded.Render(MarkupFormat.Ansi)).Contains("\u001b[38;2;255;0;0m");
	}

	[Test]
	public async Task Box_ADividerWithNoBorderOfItsOwn_TakesTheBoxs()
		=> await Assert.That(TrimLines(await Eval("box(a%r[rule()]%rb,,9,{{\"border\":\"double\"}})")))
			.IsEqualTo(Lines("╔═══════╗", "║ a     ║", "╠═══════╣", "║ b     ║", "╚═══════╝"));

	/// <summary>Each layout function's help lists exactly the option keys its schema takes.</summary>
	[Test]
	public async Task TheHelpListsEveryOptionEachFunctionTakes()
	{
		var help = File.ReadAllLines(Path.Join(TestPaths.Helpfiles.FullName, "layout-functions.md"));
		var borderPieces = OptionKeys(Topic(help, "Layout Borders").SkipWhile(line => !line.StartsWith("Any part of the style", StringComparison.Ordinal)).Skip(1));

		foreach (var (function, keys) in SharpMUSH.Implementation.Functions.Functions.LayoutOptionKeys)
		{
			var topic = Topic(help, function == "datacolumns" ? "datatable()" : $"{function}()");
			var options = topic.SkipWhile(line => !line.StartsWith("Options", StringComparison.Ordinal)).Skip(1).ToArray();
			var documented = OptionKeys(options);
			if (options.TakeWhile(line => line.StartsWith("- ", StringComparison.Ordinal)).Any(line => line.Contains("[LAYOUT BORDERS]")))
				documented.UnionWith(borderPieces);

			await Assert.That(documented.Order()).IsEquivalentTo(keys.Order()).Because($"{function}() help and schema differ");
		}
	}

	/// <summary>The lines of a help topic, from its heading to the next.</summary>
	private static IEnumerable<string> Topic(string[] help, string name) =>
		help.SkipWhile(line => line != $"# {name}").Skip(1).TakeWhile(line => !line.StartsWith("# ", StringComparison.Ordinal));

	/// <summary>The keys a list of options names: every <c>`key:...`</c> or <c>`key`</c> before the dash that explains it.</summary>
	private static HashSet<string> OptionKeys(IEnumerable<string> lines) =>
		[.. lines.TakeWhile(line => line.StartsWith("- ", StringComparison.Ordinal))
			.SelectMany(line => System.Text.RegularExpressions.Regex.Matches(line.Split(" - ")[0], "`\"?([a-z]+)[^`]*`").Select(match => match.Groups[1].Value))];

	/// <summary>Every <c>&gt; think</c> example in the layout help, with the lines under it as its output.</summary>
	public static IEnumerable<Func<(string Code, string Expected)>> HelpExamples()
	{
		var lines = File.ReadAllLines(Path.Join(TestPaths.Helpfiles.FullName, "layout-functions.md"));
		for (var i = 0; i < lines.Length; i++)
		{
			if (!lines[i].StartsWith("> think ", StringComparison.Ordinal)) continue;
			var code = lines[i]["> think ".Length..];
			var output = lines.Skip(i + 1).TakeWhile(line => !line.StartsWith('>') && !line.StartsWith("```", StringComparison.Ordinal)).ToArray();
			yield return () => (code, string.Join("\n", output));
		}
	}

	/// <summary>
	/// Each help example draws what it shows, as a client without Unicode is sent it: help examples
	/// stay ASCII, and the text round them says what a Unicode client sees instead.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(HelpExamples))]
	public async Task TheHelpExamplesShowWhatTheyDraw(string code, string expected)
		=> await Assert.That(TrimLines(AsAscii(await Eval(code)))).IsEqualTo(expected);

	private static MString AsAscii(MString text) => BlockLayout.Relayout(text, 0, new LayoutContext { AsciiOnly = true });

	/// <summary>The layout help is ASCII throughout.</summary>
	[Test]
	public async Task TheLayoutHelpIsAscii()
	{
		var help = File.ReadAllLines(Path.Join(TestPaths.Helpfiles.FullName, "layout-functions.md"));
		await Assert.That(help.Where(line => line.Any(c => c > '\u007f'))).IsEmpty();
	}

	/// <summary>
	/// The options are one JSON object, written inside a second pair of braces or built with
	/// <c>json()</c>; a string keeps its colour and an escaped quote is a quote.
	/// </summary>
	[Test]
	public async Task OptionsAreAJsonObject()
	{
		var literal = await Eval("box(Hi,T,16,{{\"border\":\"double\",\"pad\":2}})");
		var built = await Eval("box(Hi,T,16,json(object,border,\"double\",pad,2))");
		await Assert.That(built.ToPlainText()).IsEqualTo(literal.ToPlainText());
		await Assert.That(literal.ToPlainText()).StartsWith("╔");

		await Assert.That(TrimLines(await Eval("box(x,Q,14,{{\"open\":\"\\\\\"\",\"close\":\"\\\\\"\"}})")).Split('\n')[0]).Contains("\"Q\"");

		var coloured = (await Eval("box(x,,10,{{\"top\":\"[ansi(hb,=)]\"}})")).Render(MarkupFormat.Ansi);
		await Assert.That(coloured).Contains("\u001b[");
	}

	/// <summary>
	/// The border options on a layout that holds others set the border of every box and rule inside
	/// it that names none; one that names its own keeps it.
	/// </summary>
	[Test]
	[Arguments("flex({{\"border\":\"double\"}},box(a,,8),box(b,,8,{{\"border\":\"ascii\"}}))", "╔", "+-")]
	[Arguments("fields({{\"border\":\"rounded\"}},Bio,box(text,,10))", "╭", null)]
	[Arguments("box([rule()]%r[box(inner,,10)],,16,{{\"border\":\"heavy\"}})", "┏", null)]
	[Arguments("datatable({{\"border\":\"double\",\"width\":30,\"delim\":\";\"}},A,box(x,,8))", "╔", null)]
	public async Task ABorderOnALayoutReachesTheBoxesInsideIt(string code, string inner, string? kept)
	{
		var lines = (await Eval(code)).ToPlainText().Split('\n');
		await Assert.That(lines.Any(line => line.Contains(inner))).IsTrue();
		await Assert.That(lines.Any(line => line.Contains("+="))).IsFalse().Because("no box inside fell back to layout_border");
		if (kept is not null) await Assert.That(lines.Any(line => line.Contains(kept))).IsTrue().Because("a box that names its own border keeps it");
	}

	/// <summary>
	/// A quoted space is the delimiter of the space lists other functions return, and an empty
	/// column under it splits into nothing: it is a column with no heading and no cells.
	/// </summary>
	[Test]
	[Arguments("datacolumns({{\"delim\":\" \"}},,Name Mannaz Raya)", "Mannaz")]
	[Arguments("datacolumns({{\"delim\":\" \"}},Name Mannaz Raya,)", "Raya")]
	[Arguments("datacolumns({{\"delim\":\" \"}},,)", "")]
	public async Task DataColumnsTakeAQuotedSpaceAndAnEmptyColumn(string code, string cell)
	{
		var text = (await Eval(code)).ToPlainText();
		await Assert.That(text).DoesNotStartWith("#-1");
		await Assert.That(text).Contains(cell);
	}

	/// <summary>
	/// A column that grows takes the width left over, so the table spans its width; without one the
	/// table is as wide as its cells.
	/// </summary>
	[Test]
	[Arguments("datatable({{\"width\":30,\"grow\":\"|1\"}},A|B,1|2)", 30)]
	[Arguments("datatable({{\"width\":30}},A|B,1|2)", 4)]
	public async Task AGrowingColumnFillsTheTable(string code, int width)
	{
		var lines = (await Eval(code)).ToPlainText().Split('\n');
		await Assert.That(lines.Max(line => line.TrimEnd().Length)).IsEqualTo(width);
	}

	[Test]
	[Arguments("box(x,,20,{{\"colour\":\"red\"}})", "#-1 UNKNOWN LAYOUT OPTION COLOUR")]
	[Arguments("box(x,,20,border:double)", "#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT")]
	[Arguments("box(x,,20,{{[\"border\"]}})", "#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT")]
	[Arguments("box(x,,20,{{\"pad\":{\"n\":1}}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,20,{{\"pad\":1}} extra)", "#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT")]
	[Arguments("box(x,,20,{{\"pad\":1,\"PAD\":2}})", "#-1 DUPLICATE LAYOUT OPTION PAD")]
	[Arguments("flex({{\"gap\":\"2\"}},a,b)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,20,{{\"border\":true}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,20,{{\"border\":\"wavy\"}})", "#-1 UNKNOWN BORDER STYLE")]
	[Arguments("box(x,,20,{{\"title\":\"middle\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,0)", ErrorMessages.Returns.ArgRange)]
	[Arguments("box(x,,1001)", ErrorMessages.Returns.ArgRange)]
	[Arguments("rule(x,abc)", ErrorMessages.Returns.ArgRange)]
	[Arguments("flex({{\"gap\":99}},a,b)", ErrorMessages.Returns.ArgRange)]
	[Arguments("flex({{\"vertical\":\"maybe\"}},a,b)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("item(x,wide)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("figure(a.png,,,up)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("fields(,Sex,Male,Species)", "#-1 FUNCTION (FIELDS) EXPECTS AN EVEN NUMBER OF ARGUMENTS")]
	[Arguments("fields({{\"cols\":0}},Sex,Male)", ErrorMessages.Returns.ArgRange)]
	[Arguments("fields({{\"cols\":\"x\"}},Sex,Male)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("tree({{\"guide\":\"wavy\"}},a)", "#-1 UNKNOWN GUIDE STYLE")]
	[Arguments("tree({{\"colour\":\"red\"}},a)", "#-1 UNKNOWN LAYOUT OPTION COLOUR")]
	[Arguments("gauge(a,12)", ErrorMessages.Returns.Numbers)]
	[Arguments("gauge(1,0)", ErrorMessages.Returns.ArgRange)]
	[Arguments("gauge(1,2,,{{\"show\":\"all\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("bullets(a b,,{{\"style\":\"wavy\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("bullets(a b,,{{\"start\":0}})", ErrorMessages.Returns.ArgRange)]
	[Arguments("grid(a b,,{{\"gap\":99}})", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable({{\"nowrap\":\"4\"}},A|B,1|2)", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable({{\"priority\":\"1|2|3\"}},A|B,1|2)", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable({{\"min\":\"x\"}},A|B,1|2)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("badge(x,purple)", "#-1 UNKNOWN BADGE KIND")]
	[Arguments("gradient(x,h|r)", "#-1 UNKNOWN COLOR")]
	[Arguments("gradient(x,r|g,{{\"rgb\":\"\"}})", "#-1 UNKNOWN LAYOUT OPTION RGB")]
	[Arguments("gradient(x,r|g,{{\"space\":\"rgb\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("gradient(x,r|g,{{\"flow\":\"sideways\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("gradient(x,r|g,{{\"repeat\":0}})", ErrorMessages.Returns.ArgRange)]
	[Arguments("gradient(x,r|g,{{\"gradient\":\"b\"}})", "#-1 UNKNOWN LAYOUT OPTION GRADIENT")]
	[Arguments("gauge(1,2,,{{\"gradient\":\"r\",\"space\":\"rgb\"}})", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("datacolumns({{\"nowrap\":\"3\"}},A|1,B|2)", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable({{\"grow\":\"101\"}},A|B,1|2)", ErrorMessages.Returns.ArgRange)]
	public async Task ABadArgumentIsRefused(string code, string error)
		=> await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(error);
}
