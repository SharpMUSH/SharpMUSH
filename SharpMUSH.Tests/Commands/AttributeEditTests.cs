using SharpMUSH.Implementation.Commands;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The value <c>@edit</c>'s <c>- Set:</c> line shows: <c>edit_helper</c> (<c>src/set.c:826-890</c>) and
/// <c>regedit_helper</c> (<c>src/set.c:1105-1121</c>) wrap each replacement in <c>ANSI_HILITE</c> ...
/// <c>ANSI_END</c>, which a colour client sees as <c>ESC[1m</c> ... reset.
/// </summary>
public class AttributeEditTests
{
	private const string Hilite = "\u001b[1m";

	[Test]
	public async ValueTask EachReplacementIsHighlighted()
	{
		var edit = AttributeEdit.Simple("foo bar foo", "foo", "baz", firstOnly: false);

		var ansi = edit.Shown.Render(MarkupFormat.Ansi);

		await Assert.That(edit.Text).IsEqualTo("baz bar baz");
		await Assert.That(edit.Shown.ToPlainText()).IsEqualTo("baz bar baz");
		await Assert.That(ansi).StartsWith($"{Hilite}baz");
		await Assert.That(ansi.Split(Hilite).Length - 1).IsEqualTo(2);
		await Assert.That(ansi).Contains(" bar ");
		await Assert.That(ansi.IndexOf(" bar ", StringComparison.Ordinal))
			.IsGreaterThan(ansi.IndexOf("baz", StringComparison.Ordinal));
	}

	[Test]
	public async ValueTask FirstOnlyHighlightsTheOneReplacement()
	{
		var edit = AttributeEdit.Simple("foo bar foo", "foo", "baz", firstOnly: true);

		var ansi = edit.Shown.Render(MarkupFormat.Ansi);

		await Assert.That(edit.Text).IsEqualTo("baz bar foo");
		await Assert.That(ansi.Split(Hilite).Length - 1).IsEqualTo(1);
		await Assert.That(ansi).EndsWith(" bar foo");
	}

	[Test]
	[Arguments("$", "foo>", "foo")]
	[Arguments("^", "<foo", "")]
	public async ValueTask AppendAndPrependHighlightWhatTheyAdd(string search, string expected, string plainPrefix)
	{
		var edit = AttributeEdit.Simple("foo", search, search == "$" ? ">" : "<", firstOnly: false);

		var ansi = edit.Shown.Render(MarkupFormat.Ansi);

		await Assert.That(edit.Text).IsEqualTo(expected);
		await Assert.That(ansi).StartsWith(plainPrefix + Hilite);
	}

	[Test]
	public async ValueTask AMissIsShownPlainAndUnmatched()
	{
		var edit = AttributeEdit.Simple("nothing here", "foo", "baz", firstOnly: false);

		await Assert.That(edit.Matched).IsFalse();
		await Assert.That(edit.Shown.Render(MarkupFormat.Ansi)).IsEqualTo("nothing here");
	}

	/// <summary><c>regedit_helper</c> highlights a replacement only when it is not empty (<c>src/set.c:1107</c>).</summary>
	[Test]
	public async ValueTask ARegexpDeletionIsNotHighlighted()
	{
		var matches = System.Text.RegularExpressions.Regex.Matches("a-b-c", "-").ToArray();

		var edit = AttributeEdit.Spliced("a-b-c", matches, ["", ""], 0);

		await Assert.That(edit.Text).IsEqualTo("abc");
		await Assert.That(edit.Shown.Render(MarkupFormat.Ansi)).IsEqualTo("abc");
	}

	[Test]
	public async ValueTask HtmlRendersTheSameHighlight()
	{
		var edit = AttributeEdit.Simple("foo", "foo", "baz", firstOnly: false);

		await Assert.That(edit.Shown.Render(MarkupFormat.Html)).Contains("baz");
		await Assert.That(edit.Shown.Render(MarkupFormat.Html)).IsNotEqualTo("baz");
	}
}
