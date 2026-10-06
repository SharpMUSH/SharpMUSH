using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// <c>flex</c> / <c>item</c> layouts (<see cref="MarkdownFlexExtension"/>): the HTML the portal styles, the columns the
/// terminal prints, and the fence problems the editor lists.
/// </summary>
public class MarkdownFlexTests
{
	private const string TwoColumns = ":::: flex\n::: item\nLeft\n:::\n::: item\nRight\n:::\n::::";

	private static string Html(string markdown) => new WikiMarkdigPipeline().RenderToHtml(markdown);

	private static string[] Lines(string markdown, int width) =>
		RecursiveMarkdownHelper.RenderMarkdown(markdown, width).ToPlainText().Split('\n');

	[Test]
	public async Task Html_FlexAndItems_CarryTheirSettingsAsClassesAndNumbers()
	{
		var html = Html(":::: flex {gap=3 align=center justify=between wrap=no}\n::: item {grow=2 basis=30% min=10 align=end}\nA\n:::\n::::");

		await Assert.That(html).Contains("<div class=\"md-flex md-row md-gap-3 md-align-center md-justify-between md-nowrap\">");
		await Assert.That(html).Contains("<div class=\"md-item md-fixed md-self-end\" style=\"--md-grow:2;--md-min:10;--md-basis:30%\">");
	}

	/// <summary>A setting off the allowlist, or a value that is not one, never reaches the HTML.</summary>
	[Test]
	[Arguments(":::: flex {style=\"color:red\" onclick=alert(1)}\n::: item\nA\n:::\n::::")]
	[Arguments(":::: flex\n::: item {basis=\"30%;background:url(x)\" grow=\"1;color:red\"}\nA\n:::\n::::")]
	public async Task Html_UnknownOrBadSettings_AreDropped(string markdown)
	{
		var html = Html(markdown);

		await Assert.That(html).DoesNotContain("color:red");
		await Assert.That(html).DoesNotContain("alert(1)");
		await Assert.That(html).DoesNotContain("url(");
		await Assert.That(html).Contains("style=\"--md-grow:1;--md-min:24\"");
	}

	/// <summary>A flex inside an item, with a live listing and a centred block in the items.</summary>
	[Test]
	public async Task Html_NestedLayout_KeepsEveryLayer()
	{
		var html = Html("::::: flex\n:::: item\n::: recent 5\n:::\n::::\n:::: item\n::: center\nHi\n:::\n::::\n:::::");

		await Assert.That(html).Matches("(?s)md-flex.*md-item.*data-directive=\"recent\".*md-item.*<div class=\"center\">.*Hi");
	}

	[Test]
	public async Task Html_ContentOutsideAnItem_BecomesAnItem()
	{
		var html = Html(":::: flex\nStray\n::::");

		await Assert.That(html).Contains("<div class=\"md-item\"");
		await Assert.That(html).Contains("<p>Stray</p>");
	}

	[Test]
	public async Task Terminal_TwoItems_PrintSideBySide()
	{
		var lines = Lines(TwoColumns, 60);

		// (60 - 2 gap) / 2 = 29 columns each.
		await Assert.That(lines).IsEquivalentTo([$"Left{new string(' ', 25)}  Right"]);
	}

	[Test]
	public async Task Terminal_GrowAndBasis_ShareTheWidth()
	{
		var lines = Lines(":::: flex {gap=1}\n::: item {basis=10 min=4}\nA\n:::\n::: item {grow=3 min=4}\nB\n:::\n::: item {min=4}\nC\n:::\n::::", 50);

		// 50 - 2 gaps = 48; 10 fixed; 38 shared 3:1 = 28 (+1 remainder) and 9.
		await Assert.That(lines).IsEquivalentTo([$"A{new string(' ', 9)} B{new string(' ', 28)} C"]);
	}

	[Test]
	public async Task Terminal_ItemsBelowTheirMinimum_Stack()
	{
		var lines = Lines(TwoColumns, 40);

		await Assert.That(lines).IsEquivalentTo(["Left", "", "Right"]);
	}

	[Test]
	public async Task Terminal_WrapNo_KeepsNarrowItemsSideBySide()
	{
		var lines = Lines(":::: flex {wrap=no gap=1}\n::: item\nLeft\n:::\n::: item\nRight\n:::\n::::", 21);

		await Assert.That(lines).IsEquivalentTo(["Left       Right"]);
	}

	[Test]
	public async Task Terminal_ColumnDirection_Stacks()
	{
		var lines = Lines(":::: flex {direction=column}\n::: item\nTop\n:::\n::: item\nBottom\n:::\n::::", 78);

		await Assert.That(lines).IsEquivalentTo(["Top", "", "Bottom"]);
	}

	/// <summary>A percentage basis in a row is of the width left after the gaps, as in the terminal.</summary>
	[Test]
	public async Task Html_PercentBasis_LeavesRoomForTheGaps()
	{
		var html = Html(":::: flex {gap=3}\n::: item {basis=50%}\nA\n:::\n::: item {basis=50%}\nB\n:::\n::::");

		await Assert.That(html).Contains("--md-basis:calc((100% - 1.5rem) * 50 / 100)");
	}

	[Test]
	public async Task Html_PercentBasisInAColumn_IsPlain()
	{
		var html = Html(":::: flex {direction=column}\n::: item {basis=50%}\nA\n:::\n::: item\nB\n:::\n::::");

		await Assert.That(html).Contains("--md-basis:50%\"");
	}

	/// <summary>A basis stays a width when the items stack, as the portal keeps it in a column layout.</summary>
	[Test]
	public async Task Terminal_ColumnDirection_KeepsTheBasisAsAWidth()
	{
		var lines = Lines(":::: flex {direction=column}\n::: item {basis=10 min=4}\nalpha beta gamma\n:::\n::: item\nBottom\n:::\n::::", 78);

		await Assert.That(lines).IsEquivalentTo(["alpha beta", "gamma", "", "Bottom"]);
	}

	/// <summary>The gap comes out of the row before the percentages, so two halves share one line.</summary>
	[Test]
	public async Task Terminal_TwoHalves_FitBesideTheirGap()
	{
		var lines = Lines(":::: flex\n::: item {basis=50% min=4}\nLeft\n:::\n::: item {basis=50% min=4}\nRight\n:::\n::::", 40);

		// (40 - 2 gap) * 50% = 19 columns each.
		await Assert.That(lines).IsEquivalentTo([$"Left{new string(' ', 15)}  Right"]);
	}

	/// <summary>Text wraps inside its column, and the shorter column is padded so the next one stays in line.</summary>
	[Test]
	public async Task Terminal_LongText_WrapsInsideItsColumn()
	{
		var lines = Lines(":::: flex {gap=1}\n::: item {min=4}\nalpha beta gamma\n:::\n::: item {min=4}\nZ\n:::\n::::", 21);

		await Assert.That(lines).IsEquivalentTo(["alpha beta Z", "gamma"]);
	}

	/// <summary>A list item that wraps in its column hangs under its text, not its bullet.</summary>
	[Test]
	public async Task Terminal_WrappedListItem_HangsUnderItsText()
	{
		var lines = Lines(":::: flex {gap=1}\n::: item {min=4}\n- alpha beta gamma\n:::\n::: item {min=4}\nZ\n:::\n::::", 25);

		await Assert.That(lines).IsEquivalentTo(["* alpha beta Z", "  gamma"]);
	}

	[Test]
	public async Task Terminal_AlignEnd_PutsTheShortColumnAtTheBottom()
	{
		var lines = Lines(":::: flex {gap=1 align=end}\n::: item {min=4}\nalpha beta gamma\n:::\n::: item {min=4}\nZ\n:::\n::::", 21);

		await Assert.That(lines).IsEquivalentTo(["alpha beta", "gamma      Z"]);
	}

	/// <summary>A centred block inside an item centres in the item's width, not the screen's.</summary>
	[Test]
	public async Task Terminal_CenterInsideAnItem_CentresInTheColumn()
	{
		var lines = Lines("::::: flex {gap=0}\n:::: item {min=4}\n::: center\nab\n:::\n::::\n:::: item {min=4}\nX\n::::\n:::::", 20);

		await Assert.That(lines).IsEquivalentTo(["    ab    X"]);
	}

	[Test]
	public async Task FenceCheck_WellNestedLayout_HasNoIssues()
	{
		var issues = MarkdownFenceCheck.Check("::::: flex\n:::: item\n::: recent 5\n:::\n::::\n:::: item\nB\n::::\n:::::");

		await Assert.That(issues).IsEmpty();
	}

	/// <summary>Giving the item the same colons as the listing in it: the listing's close ends the item too.</summary>
	[Test]
	public async Task FenceCheck_SameColonsInside_ReportsTheUnclosedBlockAndTheStrayFence()
	{
		var issues = MarkdownFenceCheck.Check(":::: flex\n::: item\n::: recent 5\n:::\nafter\n:::\n::::");

		await Assert.That(issues).Contains(new MarkdownFenceIssue(3, MarkdownFenceProblem.Unclosed));
		await Assert.That(issues).Contains(new MarkdownFenceIssue(6, MarkdownFenceProblem.Unnamed));
		await Assert.That(issues).Contains(new MarkdownFenceIssue(5, MarkdownFenceProblem.OutsideItem));
	}

	/// <summary>Colons that grow inwards: the item's close ends the flex.</summary>
	[Test]
	public async Task FenceCheck_InvertedColons_ReportsTheStrayFence()
	{
		var issues = MarkdownFenceCheck.Check("::: flex\n::::: item\nA\n:::::\n::::: item\nB\n:::::\n:::");

		await Assert.That(issues).Contains(new MarkdownFenceIssue(5, MarkdownFenceProblem.ItemOutsideFlex));
		await Assert.That(issues.Any(issue => issue.Problem == MarkdownFenceProblem.Unnamed)).IsTrue();
	}

	[Test]
	public async Task FenceCheck_UnclosedBlock_IsReported()
	{
		var issues = MarkdownFenceCheck.Check("Intro\n\n::: center\nHi");

		await Assert.That(issues).IsEquivalentTo([new MarkdownFenceIssue(3, MarkdownFenceProblem.Unclosed)]);
	}
}
