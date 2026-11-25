using System.Text.Json;
using Bunit;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Exporting a stretch of the play terminal's longer log: the kept lines between two times, written as plain text or
/// as one HTML page that opens on its own and does nothing but show.
/// </summary>
public class TerminalLogExportTests : BunitContext
{
	private static readonly TerminalIdentity Ada = new("ada@example.com", "#7:1700000000000");
	private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Local);

	public TerminalLogExportTests() => JSInterop.Mode = JSRuntimeMode.Loose;

	private static TerminalLine Server(string text, DateTime at, string? html = null) =>
		new(at, text, html ?? text, TerminalLineSource.Server);

	private static string Stored(params TerminalLine[] lines) =>
		JsonSerializer.Serialize(lines.Select(l => new { t = l.Text, h = l.Html, at = new DateTimeOffset(l.Timestamp).ToUnixTimeMilliseconds() }));

	[Test]
	public async Task Between_WhileOff_ReadsNothing()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);

		var found = await log.BetweenAsync(Ada, new DateTimeOffset(Noon.AddHours(-1)), new DateTimeOffset(Noon));

		await Assert.That(found is NotFound).IsTrue();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.TerminalLog.between")).IsFalse();
	}

	[Test]
	public async Task Between_AsksForTheCharactersRange_InMilliseconds()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true)
			.SetResult(Stored(Server("one", Noon.AddMinutes(-30)), Server("two", Noon.AddMinutes(-10))));
		var from = new DateTimeOffset(Noon.AddHours(-1));
		var to = new DateTimeOffset(Noon);

		var found = await log.BetweenAsync(Ada, from, to);

		await Assert.That(found is IReadOnlyList<TerminalLine> lines && lines.Select(l => l.Text).SequenceEqual(["one", "two"])).IsTrue();
		var asked = JSInterop.Invocations.Single(i => i.Identifier == "SharpMUSH.TerminalLog.between").Arguments;
		await Assert.That((string?)asked[0]).IsEqualTo(TerminalLog.KeyFor(Ada));
		await Assert.That((long)asked[1]!).IsEqualTo(from.ToUnixTimeMilliseconds());
		await Assert.That((long)asked[2]!).IsEqualTo(to.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task Between_NothingKept_IsNotFound()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult("[]");

		var found = await log.BetweenAsync(Ada, new DateTimeOffset(Noon.AddHours(-1)), new DateTimeOffset(Noon));

		await Assert.That(found is NotFound).IsTrue();
	}

	[Test]
	public async Task Between_StoreOutOfReach_IsAnError()
	{
		var log = new TerminalLog(JSInterop.JSRuntime);
		await log.SetAsync(true);
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult(null);

		var found = await log.BetweenAsync(Ada, new DateTimeOffset(Noon.AddHours(-1)), new DateTimeOffset(Noon));

		await Assert.That(found is Error<string>).IsTrue();
	}

	[Test]
	public async Task Text_StampsEachLine_LinesUpWhatALineBreaksInto_AndDropsPadding()
	{
		var text = TerminalLogExport.Text([Server("Ada says, \"hi\"", Noon), Server("+---+   \n| a |   ", Noon.AddSeconds(5))],
			"Ada", Noon.AddHours(-1), Noon, times: true);

		await Assert.That(text).IsEqualTo(
			"Ada, 2026-10-10 11:00 to 2026-10-10 12:00\n\n" +
			"[2026-10-10 12:00:00] Ada says, \"hi\"\n" +
			"[2026-10-10 12:00:05] +---+\n" +
			"                      | a |\n");
	}

	[Test]
	public async Task Text_WithoutTimes_IsTheLinesAlone()
	{
		var text = TerminalLogExport.Text([Server("one", Noon), Server("two", Noon)], "Ada", Noon.AddHours(-1), Noon, times: false);

		await Assert.That(text).IsEqualTo("Ada, 2026-10-10 11:00 to 2026-10-10 12:00\n\none\ntwo\n");
	}

	[Test]
	public async Task Html_IsOnePageThatOnlyShows()
	{
		var style = new TerminalLogExport.Style(".ms-bold { font-weight: bold; } </style><script>alert(1)</script>",
			new Dictionary<string, string> { ["--text"] = "#eeeeee", ["--surface"] = "red; } body { display: none" });

		var html = TerminalLogExport.Html([Server("hi", Noon, "<span style=\"color: #ff0000\">hi</span>")],
			"Ada <Lovelace>", Noon.AddHours(-1), Noon, times: true, style);

		await Assert.That(html).StartsWith("<!doctype html>");
		await Assert.That(html).Contains("Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src https: data:\"");
		await Assert.That(html).Contains("<h1>Ada &lt;Lovelace&gt;, 2026-10-10 11:00 to 2026-10-10 12:00</h1>");
		await Assert.That(html).Contains("<span class=\"text\"><span style=\"color: #ff0000\">hi</span></span>");
		await Assert.That(html).Contains("<time datetime=\"2026-10-10T12:00:00.000");
		await Assert.That(html).Contains("--text: #eeeeee;");
		await Assert.That(html).DoesNotContain("display: none").Because("a value that could end the rule is left out");
		await Assert.That(html).DoesNotContain("</style><script>").Because("the stylesheet cannot close its style element");
	}

	[Test]
	public async Task Html_KeepsTheThemesMarkupColours_AndLoadsPicturesFromThePortal()
	{
		var style = new TerminalLogExport.Style(null,
			new Dictionary<string, string> { ["--ms-ansi-9"] = "#8f4347", ["--ms-x;}"] = "red" }, "https://game.example.com");

		var html = TerminalLogExport.Html([Server("Hall", Noon, "<img src=\"/media/hall.png\" alt=\"Hall\"><img src=\"https://cdn.example.com/a.png\">")],
			"Ada", Noon.AddHours(-1), Noon, times: false, style);

		await Assert.That(html).Contains("--ms-ansi-9: #8f4347;");
		await Assert.That(html).DoesNotContain("--ms-x").Because("a name that could end the rule is left out");
		await Assert.That(html).Contains("img-src https: data: https://game.example.com\"");
		await Assert.That(html).Contains("<img src=\"https://game.example.com/media/hall.png\" alt=\"Hall\">");
		await Assert.That(html).Contains("<img src=\"https://cdn.example.com/a.png\">");
	}

	[Test]
	public async Task Html_WithoutTimes_HasNoTimes()
	{
		var html = TerminalLogExport.Html([Server("hi", Noon)], "Ada", Noon.AddHours(-1), Noon, times: false, TerminalLogExport.Style.None);

		await Assert.That(html).DoesNotContain("<time");
		await Assert.That(html).Contains("<div class=\"line\"><span class=\"text\">hi</span></div>");
	}

	[Test]
	public async Task FileName_NamesTheCharacterAndRange_WithoutCharactersAFileCannotHold()
	{
		await Assert.That(TerminalLogExport.FileName("Ada/Lovelace?", Noon.AddHours(-1), Noon, TerminalLogFormat.Text))
			.IsEqualTo("AdaLovelace 2026-10-10 1100 to 2026-10-10 1200.txt");
		await Assert.That(TerminalLogExport.FileName("", Noon.AddHours(-1), Noon, TerminalLogFormat.Html))
			.IsEqualTo("log 2026-10-10 1100 to 2026-10-10 1200.html");
	}
}
