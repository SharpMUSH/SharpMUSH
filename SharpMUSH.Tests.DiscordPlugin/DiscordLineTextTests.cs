using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordLineTextTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private static SharpFeedMessage Message(string style, string text, string name = "Alice", string display = "",
		MString? line = null)
		=> Message(style, MString.Plain(text), name, display, line);

	private static SharpFeedMessage Message(string style, MString text, string name = "Alice", string display = "",
		MString? line = null)
		=> new(1, "discord", "123", Now, new DBRef(5), name, new DBRef(5), name, null, "", style, text, display, line);

	[Test]
	[Arguments("say", "Hello there.", "Hello there.")]
	[Arguments("pose", "waves.", "*Alice waves.*")]
	[Arguments("semipose", "'s here.", "*Alice's here.*")]
	[Arguments("emit", "The lights flicker.", "The lights flicker.")]
	public async Task EachStyle_ReadsAsTheGameShowsIt(string style, string text, string expected)
	{
		var line = DiscordLineText.From(Message(style, text));

		await Assert.That(line.Speaker).IsEqualTo("Alice");
		await Assert.That(line.Text).IsEqualTo(expected);
		await Assert.That(line.At).IsEqualTo(Now);
	}

	[Test]
	public async Task MarkdownInTheText_IsEscaped_SoItReadsAsTyped()
	{
		var line = DiscordLineText.From(Message("say", "a *b* c"));

		await Assert.That(line.Text).IsEqualTo(@"a \*b\* c");
	}

	[Test]
	public async Task Bold_BecomesDiscordBold_AndColourIsDropped()
	{
		var text = MarkupText.Concat(
			MarkupText.Concat(MarkupText.Wrap(AnsiMarkup.Create(bold: true), MarkupText.Plain("Loud")), MarkupText.Plain(" and ")),
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain("red")));

		var line = DiscordLineText.From(Message("say", text));

		await Assert.That(line.Text).IsEqualTo("**Loud** and red");
	}

	[Test]
	public async Task TheChosenName_IsWhatTheLineGoesOutUnder()
	{
		var line = DiscordLineText.From(Message("pose", "salutes.", display: "Captain"));

		await Assert.That(line.Speaker).IsEqualTo("Captain");
		await Assert.That(line.Text).IsEqualTo("*Captain salutes.*");
	}

	[Test]
	public async Task ALineTheSystemFormatted_GoesOutAsDelivered()
	{
		var line = DiscordLineText.From(Message("say", "hi", line: MString.Plain("[Public] Alice says, \"hi\"")));

		await Assert.That(line.Text).IsEqualTo("\\[Public\\] Alice says, \"hi\"");
	}

	[Test]
	[Arguments("Discord Fan", "D​iscord Fan")]
	[Arguments("clyde", "c​lyde")]
	[Arguments("  Two   words ", "Two words")]
	[Arguments("   ", DiscordLineText.FallbackSpeaker)]
	public async Task Speaker_IsANameDiscordTakes(string name, string expected)
		=> await Assert.That(DiscordLineText.Speaker(name)).IsEqualTo(expected);

	[Test]
	public async Task Speaker_IsCutTo80Characters()
		=> await Assert.That(DiscordLineText.Speaker(new string('a', 100)).Length).IsEqualTo(DiscordLineText.MaxSpeaker);
}
