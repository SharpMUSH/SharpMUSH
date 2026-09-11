using MarkupString;
using SharpMUSH.Plugins.Scene.Commands;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// How <c>@scene/addpose</c> and <c>@scene/editpose</c> carve their comma-separated argument list.
///
/// <para>The last field is the pose's content and belongs to whoever wrote it, whitespace included.
/// The fields before it are dbrefs, roles and keywords, compared as text, where a space after the
/// comma is typing rather than meaning — so those are trimmed and the content is not.</para>
/// </summary>
public class SceneCommandHelperTests
{
	[Test]
	public async Task SplitFieldsKeepingMarkup_KeepsTheContentsLeadingWhitespace()
	{
		var (fields, content) = SceneCommandHelper.SplitFieldsKeepingMarkup(
			MarkupText.Plain("#3, Mira ,#10,pose,,   She steps into the light."), 6);

		await Assert.That(content.ToPlainText()).IsEqualTo("   She steps into the light.")
			.Because("an indented pose is written with %b for exactly this reason; trimming it here undid that");
		await Assert.That(fields[1]).IsEqualTo("Mira")
			.Because("the fields before the content are keywords, and a space around one is typing");
	}

	[Test]
	public async Task SplitFieldsKeepingMarkup_KeepsTheContentsTrailingWhitespaceAndBlankLines()
	{
		var (_, content) = SceneCommandHelper.SplitFieldsKeepingMarkup(
			MarkupText.Plain("#3,,#10,pose,,\n\nA beat, then:\n"), 6);

		await Assert.That(content.ToPlainText()).IsEqualTo("\n\nA beat, then:\n")
			.Because("a pose that opens on a blank line and closes on a break means both");
	}

	[Test]
	public async Task SplitFieldsKeepingMarkup_LeavesCommasInsideTheContentAlone()
	{
		var (fields, content) = SceneCommandHelper.SplitFieldsKeepingMarkup(
			MarkupText.Plain("#3,,#10,say,,Mira says, \"Well, then.\""), 6);

		await Assert.That(fields[3]).IsEqualTo("say");
		await Assert.That(content.ToPlainText()).IsEqualTo("Mira says, \"Well, then.\"");
	}

	[Test]
	public async Task SplitFields_StillTrimsATrailingTitleField()
	{
		// @scene/create <room>,<owner>[,<title>] — the title is not prose the way a pose is, and a
		// space after the comma there is how the line was typed.
		var fields = SceneCommandHelper.SplitFields(MarkupText.Plain("#5, #3, A Quiet Night "), 3);

		await Assert.That(fields[2]).IsEqualTo("A Quiet Night");
	}

	[Test]
	public async Task SplitFieldsKeepingMarkup_MissingTrailingFieldsAreEmpty()
	{
		var (fields, content) = SceneCommandHelper.SplitFieldsKeepingMarkup(MarkupText.Plain("#3,,#10"), 6);

		await Assert.That(fields[4]).IsEqualTo(string.Empty);
		await Assert.That(content.ToPlainText()).IsEqualTo(string.Empty);
	}
}
