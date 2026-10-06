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
		var result = await Eval("box(flex(sep:\" | \",item(Sex: Male%rSpecies: Human,35),item(Job: Dark Warrior%rOnline: 1h,36))%r[rule(Quote)]%rHooooo?,Mannaz Byron,78,open:\"<< \" close:\" >>\")");

		await Assert.That(result.ToPlainText()).IsEqualTo(Lines(
			"+=============================<< Mannaz Byron >>=============================+",
			"| Sex: Male                           | Job: Dark Warrior                    |",
			"| Species: Human                      | Online: 1h                           |",
			"+==================================< Quote >=================================+",
			"| Hooooo?                                                                    |",
			"+============================================================================+"));
	}

	/// <summary>The portal draws the same layout as page structure, not as box art.</summary>
	[Test]
	public async Task Box_IsACardInTheBrowser()
	{
		var html = (await Eval("box(flex(sep:\" | \",item(Left,20),item(Right,20))%r[rule(Quote)]%rHooooo?,Title,50)")).Render(MarkupFormat.Html);

		await Assert.That(html).StartsWith("<div class=\"ms-layout\" style=\"max-width:50ch\"><fieldset class=\"ms-box ms-border-mush\"><legend class=\"ms-box-title\">Title</legend>");
		await Assert.That(html).Contains("<div class=\"ms-flex ms-divided\"");
		await Assert.That(html).Contains("<div class=\"ms-divider ms-border-mush\" role=\"separator\"><span class=\"ms-rule-title\">Quote</span></div>");
		await Assert.That(html).DoesNotContain("+===");
	}

	[Test]
	public async Task Box_TakesAPresetAndItsOwnPieces()
	{
		await Assert.That((await Eval("box(Hello there.,,30,border:rounded)")).ToPlainText()).IsEqualTo(Lines(
			"╭────────────────────────────╮",
			"│ Hello there.               │",
			"╰────────────────────────────╯"));
		await Assert.That((await Eval("box(x,,20,top:-= corner:*)")).ToPlainText()).IsEqualTo(Lines(
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
		=> await Assert.That(TrimLines(await Eval("flex(sep:\" | \" width:40,item(Strength%rAgility,18),item(High%rLow,19))")))
			.IsEqualTo(Lines(
				"Strength           | High",
				"Agility            | Low"));

	[Test]
	public async Task Flex_StacksItemsThatDoNotFit()
		=> await Assert.That(TrimLines(await Eval("flex(width:20,item(Left,15,15),item(Right,15,15))")))
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
		=> await Assert.That(TrimLines(await Eval("fields(width:40,Sex,Male,Species,Human,Origin,Super Robot Wars AG)")))
			.IsEqualTo(Lines(
				"Sex:     Male",
				"Species: Human",
				"Origin:  Super Robot Wars AG"));

	[Test]
	public async Task Fields_TakeALeaderAndRightAlignedLabels()
	{
		await Assert.That(TrimLines(await Eval("fields(width:40 leader:.,Sex,Male,Species,Human)")))
			.IsEqualTo(Lines("Sex....: Male", "Species: Human"));
		await Assert.That(TrimLines(await Eval("fields(width:40 align:right,Sex,Male,Species,Human)")))
			.IsEqualTo(Lines("    Sex: Male", "Species: Human"));
	}

	/// <summary>The finger box the layout functions were made for, with its key/value pairs as fields.</summary>
	[Test]
	public async Task Fields_InsideABox_MakeAFingerSheet()
	{
		var result = await Eval("box(fields(cols:2,Sex,Male,Species,Human,Job,Dark Warrior,Online,1h)%r[rule(Quote)]%rHooooo?,Mannaz Byron,60)");

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
		var result = await Eval("tree(width:30,node(Channels,node(Public,+chat,+ooc),node(Staff,+admin)))");

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
		await Assert.That(TrimLines(await Eval("tree(width:30 guide:ascii,node(Mail,Inbox,Sent))")))
			.IsEqualTo(Lines("Mail", "|- Inbox", "`- Sent"));
		await Assert.That(TrimLines(await Eval("tree(width:30 branch:\"+> \" last:\"*> \",node(Mail,Inbox,Sent))")))
			.IsEqualTo(Lines("Mail", "+> Inbox", "*> Sent"));
	}

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

	[Test]
	[MethodDataSource(nameof(HelpExamples))]
	public async Task TheHelpExamplesShowWhatTheyDraw(string code, string expected)
		=> await Assert.That(TrimLines(await Eval(code))).IsEqualTo(expected);

	[Test]
	[Arguments("box(x,,20,colour:red)", "#-1 UNKNOWN LAYOUT OPTION COLOUR")]
	[Arguments("box(x,,20,border:wavy)", "#-1 UNKNOWN BORDER STYLE")]
	[Arguments("box(x,,20,title:middle)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,0)", ErrorMessages.Returns.ArgRange)]
	[Arguments("box(x,,1001)", ErrorMessages.Returns.ArgRange)]
	[Arguments("rule(x,abc)", ErrorMessages.Returns.ArgRange)]
	[Arguments("flex(gap:99,a,b)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("item(x,wide)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("figure(a.png,,,up)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("fields(,Sex,Male,Species)", "#-1 FUNCTION (FIELDS) EXPECTS AN EVEN NUMBER OF ARGUMENTS")]
	[Arguments("fields(cols:0,Sex,Male)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("tree(guide:wavy,a)", "#-1 UNKNOWN GUIDE STYLE")]
	[Arguments("tree(colour:red,a)", "#-1 UNKNOWN LAYOUT OPTION COLOUR")]
	[Arguments("gauge(a,12)", ErrorMessages.Returns.Numbers)]
	[Arguments("gauge(1,0)", ErrorMessages.Returns.ArgRange)]
	[Arguments("gauge(1,2,,show:all)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("bullets(a b,,style:wavy)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("bullets(a b,,start:0)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("grid(a b,,gap:99)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("datatable(nowrap:4,A|B,1|2)", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable(priority:1|2|3,A|B,1|2)", ErrorMessages.Returns.ArgRange)]
	[Arguments("datatable(min:x,A|B,1|2)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("badge(x,purple)", "#-1 UNKNOWN BADGE KIND")]
	public async Task ABadArgumentIsRefused(string code, string error)
		=> await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(error);
}
