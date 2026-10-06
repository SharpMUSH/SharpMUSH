using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The formatted input's draft: edits worked out from the field's value and caret, formatting applied over
/// existing colour, undo, the two modes, and the softcode it sends.
/// </summary>
public class ComposerDraftTests
{
	private static readonly ComposerFormat Red = new ComposerFormat.Foreground(new AnsiColor.Standard(1, false));
	private static readonly ComposerFormat Green = new ComposerFormat.Foreground(new AnsiColor.Standard(2, false));

	private static ComposerDraft Typed(string text)
	{
		var draft = new ComposerDraft();
		draft.ApplyInput(text, text.Length);
		return draft;
	}

	[Test]
	public async Task PlainText_IsSentLiterally_EverySpecialEscaped()
	{
		var draft = Typed("Hi; [wave], 100%  ok");
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { @"Hi\; \[wave\]\, 100\% %bok" });
	}

	[Test]
	public async Task ASelection_IsColoured_AndSentAsAnsi()
	{
		var draft = Typed("Well met, friend");
		draft.Apply(Red, 0, 8);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { @"[ansi(r,Well met)]\, friend" });
	}

	[Test]
	public async Task ANewColour_WinsOverTheOneAlreadyThere()
	{
		var draft = Typed("abc");
		draft.Apply(Red, 0, 3);
		draft.Apply(Green, 1, 2);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ansi(r,a)][ansi(g,b)][ansi(r,c)]" });
	}

	[Test]
	public async Task AFormatTheWholeSelectionHas_IsTurnedOff()
	{
		var draft = Typed("abc");
		draft.Apply(new ComposerFormat.Underline(), 0, 3);
		draft.Apply(new ComposerFormat.Underline(), 0, 3);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "abc" });
	}

	[Test]
	public async Task Typing_TakesTheStyleOfTheCharacterBefore()
	{
		var draft = Typed("ab");
		draft.Apply(Red, 0, 2);
		draft.ApplyInput("abc", 3);
		draft.ApplyInput("Xabc", 1);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "X[ansi(r,abc)]" });
	}

	[Test]
	public async Task TheCaret_DecidesWhichOfTwoEqualCharactersWasTyped()
	{
		var draft = Typed("aa");
		draft.Apply(Red, 0, 1);
		// "a" typed at 1, after the red one: the new character is red, not the plain one at the end.
		draft.ApplyInput("aaa", 2);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ansi(r,aa)]a" });
	}

	[Test]
	public async Task APickWithNothingSelected_StylesWhatIsTypedNext()
	{
		var draft = Typed("say ");
		draft.Apply(Green, 4, 4);
		draft.ApplyInput("say hi", 6);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "say%b[ansi(g,hi)]" });
	}

	[Test]
	public async Task ClearFormatting_TakesTheColourAway()
	{
		var draft = Typed("abc");
		draft.Apply(Red, 0, 3);
		draft.Apply(new ComposerFormat.Bold(), 0, 3);
		draft.Apply(new ComposerFormat.ClearFormatting(), 1, 3);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ansi(rh,a)]bc" });
	}

	[Test]
	public async Task Undo_PutsTheFormattingBack_AndRedoTakesItAgain()
	{
		var draft = Typed("abc");
		draft.Apply(Red, 0, 3);
		draft.ApplyInput("abcd", 4);
		await Assert.That(draft.Undo()).IsTrue();
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ansi(r,abc)]" });
		await Assert.That(draft.Undo()).IsTrue();
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "abc" });
		await Assert.That(draft.Redo()).IsTrue();
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ansi(r,abc)]" });
	}

	[Test]
	public async Task LineBreaks_AreR_OrSplitIntoOneLineEach()
	{
		var draft = Typed("one\n\n two");
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "one%r%r two" });
		await Assert.That(draft.ToSoftcode(splitLines: true)).IsEquivalentTo(new[] { "one", "%btwo" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Raw_WritesStyledStretchesAsSoftcode_AndLeavesTypedSoftcodeAlone()
	{
		var draft = Typed("[ucstr(hi)] there");
		draft.Apply(Red, 12, 17);
		draft.SetRaw(true);
		await Assert.That(draft.Value).IsEqualTo("[ucstr(hi)] [ansi(r,there)]");
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { "[ucstr(hi)] [ansi(r,there)]" });
	}

	[Test]
	public async Task Raw_WrapsTheSelectionInAnAnsiCall()
	{
		var draft = new ComposerDraft();
		draft.Load(MarkupText.Plain("@desc me=A door."), raw: true);
		var (start, end) = draft.Apply(new ComposerFormat.Foreground(new AnsiColor.Standard(1, true)), 9, 15);
		await Assert.That(draft.Value).IsEqualTo("@desc me=[ansi(hr,A door)].");
		await Assert.That(draft.Value[start..end]).IsEqualTo("A door");
	}

	[Test]
	[Arguments("Look %r here", "%r")]
	[Arguments("[ansi(r,red)] text", "[ansi(")]
	[Arguments("%bindent", "%b")]
	[Arguments("as %n says", "%n")]
	[Arguments("q %q0", "%q0")]
	public async Task Softcode_IsNoticed(string text, string found)
	{
		await Assert.That(Typed(text).SoftcodeLookalike).IsEqualTo(found);
	}

	[Test]
	[Arguments("100% sure")]
	[Arguments("[OOC] back soon")]
	[Arguments("a (quiet) aside")]
	public async Task Prose_IsNotMistakenForSoftcode(string text)
	{
		await Assert.That(Typed(text).SoftcodeLookalike).IsNull();
	}

	[Test]
	public async Task AStoredPose_EditedAndSentUnchanged_DecomposesAsBefore()
	{
		var pose = MarkupText.Concat(
			MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "Well met"),
			MarkupText.Plain(", all."));
		var draft = new ComposerDraft();
		draft.Load(pose, raw: false);
		await Assert.That(draft.ToSoftcode()).IsEquivalentTo(new[] { SoftcodeDecomposer.Decompose(pose) });
	}
}
