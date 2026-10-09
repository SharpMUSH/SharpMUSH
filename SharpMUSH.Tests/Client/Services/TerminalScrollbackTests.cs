using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// What a terminal keeps of its screen for a reload: the server's rendered lines, never an action a
/// line carried (a sound, a stop, a clear screen, a prefetch) and never what the player typed.
/// </summary>
public class TerminalScrollbackTests
{
	private static TerminalLine Server(MString markup) =>
		new(DateTime.Now, markup.ToPlainText(), markup.Render(MarkupFormat.Html), TerminalLineSource.Server);

	[Test]
	public async Task A_server_line_keeps_its_text_and_rendering()
	{
		var line = Server(MarkupText.Plain("The quay at dusk."));

		var stored = TerminalScrollback.Serialize(line).Expect<string>();
		var restored = TerminalScrollback.Parse($"[{stored}]").Single();

		await Assert.That(restored.Text).IsEqualTo("The quay at dusk.");
		await Assert.That(restored.Html).IsEqualTo(line.Html);
		await Assert.That(restored.Source).IsEqualTo(TerminalLineSource.Server);
	}

	/// <summary>
	/// Typed input can carry a password (<c>@password</c>, <c>connect</c>), and storage outlives the
	/// page; system lines describe the connection the page had, not the session.
	/// </summary>
	[Test]
	public async Task Typed_and_system_lines_are_not_kept()
	{
		await Assert.That(TerminalScrollback.Serialize(new TerminalLine(DateTime.Now, "@password a=b", TerminalLineSource.Client)) is NotFound).IsTrue();
		await Assert.That(TerminalScrollback.Serialize(new TerminalLine(DateTime.Now, "Connected.", TerminalLineSource.System)) is NotFound).IsTrue();
	}

	[Test]
	public async Task Actions_are_dropped_and_the_text_around_them_kept()
	{
		MString[] parts =
		[
			MarkupText.Plain("A bell rings. "),
			MarkupText.Sound("bell.mp3", null, null),
			MarkupText.StopSound(null),
			MarkupText.ClearScreen(),
			MarkupText.Prefetch("next.png"),
			MarkupText.Image("quay.png", "The quay", null, null, null),
			MarkupText.Plain(" Done."),
		];
		var line = new TerminalLine(DateTime.Now, "A bell rings.  Done.",
			string.Concat(parts.Select(p => p.Render(MarkupFormat.Html))), TerminalLineSource.Server);

		var restored = TerminalScrollback.Parse($"[{TerminalScrollback.Serialize(line).Expect<string>()}]").Single();

		await Assert.That(restored.Html).DoesNotContain("<audio");
		await Assert.That(restored.Html).DoesNotContain("ms-sound");
		await Assert.That(restored.Html).DoesNotContain("ms-clear");
		await Assert.That(restored.Html).DoesNotContain("prefetch");
		await Assert.That(restored.Html).Contains("A bell rings.");
		await Assert.That(restored.Html).Contains("quay.png").Because("a picture is shown, not run");
		await Assert.That(restored.Html).Contains("Done.");
	}

	/// <summary>
	/// An empty cell keeps its place: dropping it moved every later cell of its row one column left, so a
	/// restored +bbread put each board's time under New and squeezed the post titles.
	/// </summary>
	[Test]
	public async Task A_tables_empty_cells_are_kept()
	{
		var table = new MarkupString.Layout.Table(
			[new MarkupString.Layout.TableColumn(MarkupText.Empty) { Wrap = false }, new MarkupString.Layout.TableColumn(MarkupText.Plain("Title"))],
			[[MarkupText.Empty, MarkupText.Plain("The First One")]]);
		var line = Server(ServerLayout.Build(table, 78));

		var restored = TerminalScrollback.Parse($"[{TerminalScrollback.Serialize(line).Expect<string>()}]").Single();

		await Assert.That(restored.Html).IsEqualTo(line.Html);
	}

	[Test]
	public async Task A_line_that_was_only_an_action_is_not_kept()
	{
		var line = Server(MarkupText.Sound("bell.mp3", null, null));

		await Assert.That(TerminalScrollback.Serialize(line) is NotFound).IsTrue();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not json")]
	[Arguments("{\"t\":\"an object, not a list\"}")]
	public async Task Damaged_scrollback_is_none(string? stored)
	{
		await Assert.That(TerminalScrollback.Parse(stored)).IsEmpty();
	}
}
