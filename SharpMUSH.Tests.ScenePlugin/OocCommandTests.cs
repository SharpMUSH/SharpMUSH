using MarkupString;
using SharpMUSH.Plugins.Scene.Commands;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The text the <c>ooc</c> command writes. The recorded pose is <see cref="OocCommand.Line"/> — the tag
/// carries the OOC marker — and the room hears <see cref="OocCommand.Band"/>, the same line with it.
/// The command itself (emit, record, broadcast) is covered end to end in
/// <c>SharpMUSH.Tests.Integration/Scenes/SceneOocIntegrationTests.cs</c>.
/// </summary>
public class OocCommandTests
{
	[Test]
	public async Task Plain_text_is_said_with_a_colon()
	{
		await Assert.That(OocCommand.Line("Tomas", MarkupText.Plain("brb, phone")).ToPlainText())
			.IsEqualTo("Tomas: brb, phone");
	}

	[Test]
	public async Task A_colon_prefix_poses()
	{
		await Assert.That(OocCommand.Line("Tomas", MarkupText.Plain(":waves.")).ToPlainText())
			.IsEqualTo("Tomas waves.");
	}

	[Test]
	public async Task A_semicolon_prefix_semiposes()
	{
		await Assert.That(OocCommand.Line("Tomas", MarkupText.Plain(";'s back.")).ToPlainText())
			.IsEqualTo("Tomas's back.");
	}

	/// <summary>Only the first character is a prefix; a colon later in the text is text.</summary>
	[Test]
	public async Task A_colon_inside_the_text_is_text()
	{
		await Assert.That(OocCommand.Line("Tomas", MarkupText.Plain("note: brb")).ToPlainText())
			.IsEqualTo("Tomas: note: brb");
	}

	[Test]
	public async Task The_band_marks_the_line_as_ooc()
	{
		var line = OocCommand.Line("Tomas", MarkupText.Plain(":waves."));

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
