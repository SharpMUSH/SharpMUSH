using MarkupString;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordInboundTextTests
{
	private static readonly DiscordMentionNames Names = new(
		new Dictionary<ulong, string> { [11] = "Alice" },
		new Dictionary<ulong, string> { [22] = "ooc" },
		new Dictionary<ulong, string> { [33] = "Staff" });

	private static MString Parse(string content) => DiscordInboundText.Parse(content, Names);

	[Test]
	[Arguments("**bold** and *it* and _it_ and ~~gone~~", "**bold** and *it* and *it* and ~~gone~~")]
	[Arguments("*a **b** c*", "*a* ***b*** *c*")]
	[Arguments("[the wiki](https://example.com/wiki)", "[the wiki](https://example.com/wiki)")]
	public async Task Styles_BecomeTheGamesMarkup(string content, string markdown)
	{
		await Assert.That(Parse(content).Render(MarkupFormat.Markdown)).IsEqualTo(markdown);
	}

	[Test]
	public async Task Underline_IsTheGamesUnderline()
	{
		var text = Parse("__under__");

		await Assert.That(text.ToPlainText()).IsEqualTo("under");
		await Assert.That(text.Render(MarkupFormat.Ansi)).Contains("[4m");
	}

	[Test]
	[Arguments("snake_case_name stays", "snake_case_name stays")]
	[Arguments("2 * 3 * 4", "2 * 3 * 4")]
	[Arguments("**not closed", "**not closed")]
	[Arguments(@"\*escaped\*", "*escaped*")]
	[Arguments("||a spoiler||", "||a spoiler||")]
	[Arguments("`**code**` and ``a ` tick``", "**code** and a ` tick")]
	[Arguments("```cs\nvar x = 1;\n```", "var x = 1;")]
	[Arguments("```\n*kept*\n```", "*kept*")]
	public async Task OtherText_StaysAsWritten(string content, string plain)
	{
		var text = Parse(content);

		await Assert.That(text.ToPlainText()).IsEqualTo(plain);
		await Assert.That(text.Render(MarkupFormat.Ansi)).IsEqualTo(plain).Because("nothing in it is styled");
	}

	[Test]
	[Arguments("hi <@11>", "hi @Alice")]
	[Arguments("hi <@!11>", "hi @Alice")]
	[Arguments("hi <@99>", "hi @unknown-user")]
	[Arguments("see <#22>", "see #ooc")]
	[Arguments("ask <@&33>", "ask @Staff")]
	[Arguments("nice <:blobwave:123> <a:party:456>", "nice :blobwave: :party:")]
	[Arguments("at <t:1792036800:F>", "at 2026-10-15 04:00 UTC")]
	[Arguments("try </roll dice:789>", "try /roll dice")]
	[Arguments("<https://example.com/a>", "https://example.com/a")]
	[Arguments("a <b> c", "a <b> c")]
	public async Task Mentions_AndEmoji_ReadAsNames(string content, string plain)
	{
		await Assert.That(Parse(content).ToPlainText()).IsEqualTo(plain);
	}

	[Test]
	[Arguments("%r[pemit(me,hi)]", "%r[pemit(me,hi)]")]
	public async Task GameCode_IsJustText(string content, string plain)
	{
		await Assert.That(Parse(content).ToPlainText()).IsEqualTo(plain);
	}
}
