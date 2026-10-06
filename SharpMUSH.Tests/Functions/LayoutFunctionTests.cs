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
	[Arguments("box(x,,20,colour:red)", "#-1 UNKNOWN LAYOUT OPTION COLOUR")]
	[Arguments("box(x,,20,border:wavy)", "#-1 UNKNOWN BORDER STYLE")]
	[Arguments("box(x,,20,title:middle)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("box(x,,0)", ErrorMessages.Returns.ArgRange)]
	[Arguments("box(x,,1001)", ErrorMessages.Returns.ArgRange)]
	[Arguments("rule(x,abc)", ErrorMessages.Returns.ArgRange)]
	[Arguments("flex(gap:99,a,b)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("item(x,wide)", ErrorMessages.Returns.InvalidArgument)]
	[Arguments("figure(a.png,,,up)", ErrorMessages.Returns.InvalidArgument)]
	public async Task ABadArgumentIsRefused(string code, string error)
		=> await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(error);
}
