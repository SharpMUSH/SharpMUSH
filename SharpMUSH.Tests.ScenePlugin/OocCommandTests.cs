using MarkupString;
using SharpMUSH.Plugins.Scene.Commands;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The text the <c>ooc</c> command writes. The recorded pose is <see cref="OocCommand.Line"/> — the tag
/// carries the OOC marker — and the room hears <see cref="OocCommand.Band"/>, the same line with it.
/// The command itself (speech, record, broadcast) is covered end to end in
/// <c>SharpMUSH.Tests.Integration/Scenes/SceneOocIntegrationTests.cs</c>.
/// </summary>
public class OocCommandTests
{
	private static string Said(string message)
	{
		var (token, words) = OocCommand.Split(MarkupText.Plain(message));
		return OocCommand.Line(MarkupText.Plain("Tomas"), token, words).ToPlainText();
	}

	[Test]
	public async Task Plain_text_is_said_with_a_colon()
	{
		await Assert.That(Said("brb, phone")).IsEqualTo("Tomas: brb, phone");
	}

	[Test]
	public async Task A_colon_prefix_poses()
	{
		await Assert.That(Said(":waves.")).IsEqualTo("Tomas waves.");
	}

	[Test]
	public async Task A_semicolon_prefix_semiposes()
	{
		await Assert.That(Said(";'s back.")).IsEqualTo("Tomas's back.");
	}

	/// <summary>Only the first character is a prefix; a colon later in the text is text.</summary>
	[Test]
	public async Task A_colon_inside_the_text_is_text()
	{
		await Assert.That(Said("note: brb")).IsEqualTo("Tomas: note: brb");
	}

	/// <summary>The token is what SPEECHMOD sees as %1: say, pose or semipose.</summary>
	[Test]
	[Arguments("hi", "\"")]
	[Arguments(":waves", ":")]
	[Arguments(";'s", ";")]
	public async Task The_prefix_picks_the_speech_token(string message, string token)
	{
		await Assert.That(OocCommand.Split(MarkupText.Plain(message)).Token).IsEqualTo(token);
	}

	[Test]
	public async Task The_band_marks_the_line_as_ooc()
	{
		var line = OocCommand.Line(MarkupText.Plain("Tomas"), ":", MarkupText.Plain("waves."));

		await Assert.That(OocCommand.Band(line).ToPlainText()).IsEqualTo("<OOC> Tomas waves.");
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments(":")]
	[Arguments(";")]
	[Arguments(":  ")]
	public async Task Nothing_to_say_is_empty(string message)
	{
		await Assert.That(OocCommand.IsEmpty(MarkupText.Plain(message))).IsTrue();
	}

	[Test]
	public async Task Something_to_say_is_not_empty()
	{
		await Assert.That(OocCommand.IsEmpty(MarkupText.Plain(":waves"))).IsFalse();
	}
}
