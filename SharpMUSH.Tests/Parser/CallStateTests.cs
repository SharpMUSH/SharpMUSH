using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Unit tests for <see cref="CallState"/> construction and invariants.
/// </summary>
public class CallStateTests
{
	/// <summary>
	/// Regression test: using <c>with { Message = … }</c> on a CallState record preserves
	/// the original <c>ParsedMessage</c> lambda, so it returns the old (stale) value.
	/// The correct approach is to construct a new CallState so <c>ParsedMessage</c>
	/// reflects the updated message.
	/// </summary>
	[Test]
	public async ValueTask AggregatedCallState_ParsedMessageReturnsFullConcatenatedMessage()
	{
		var first = new CallState(MarkupText.Plain("Boo! "));
		var second = new CallState(MarkupText.Plain("a"));

		var concatenated = MarkupText.Concat(first.Message, second.Message);

		// The correct approach: new CallState keeps ParsedMessage consistent with Message.
		var correct = new CallState(concatenated, first.Depth);
		var correctParsed = await correct.ParsedMessage();
		await Assert.That(correctParsed?.ToPlainText()).IsEqualTo("Boo! a");

		// The buggy approach: `with` only updates Message; ParsedMessage still points at "Boo! ".
		var stale = first with { Message = concatenated };
		var staleParsed = await stale.ParsedMessage();
		await Assert.That(staleParsed?.ToPlainText()).IsNotEqualTo("Boo! a")
			.And.IsEqualTo("Boo! ");
	}

	/// <summary>
	/// A state with a deferred evaluation keeps it through a copy: <c>with</c> changes the text, not
	/// what the text evaluates to.
	/// </summary>
	[Test]
	public async ValueTask DeferredCallState_CopyKeepsItsEvaluation()
	{
		var deferred = new CallState(MarkupText.Plain("%0"), 0, null,
			() => ValueTask.FromResult<MarkupText?>(MarkupText.Plain("evaluated")));

		var copied = deferred with { Message = MarkupText.Plain("other") };
		await Assert.That((await copied.ParsedMessage())?.ToPlainText()).IsEqualTo("evaluated");
	}

	/// <summary>
	/// A plain evaluated value is bound to a call as it is; anything else is copied down to its text.
	/// </summary>
	[Test]
	public async ValueTask AsValue_ReusesAPlainValueAndCopiesAnythingElse()
	{
		var text = MarkupText.Plain("x");
		var plain = new CallState(text, 2) { HadErrors = true };
		await Assert.That(ReferenceEquals(plain.AsValue(text, 2), plain)).IsTrue();

		var deeper = plain.AsValue(text, 3);
		await Assert.That(deeper.Depth).IsEqualTo(3);
		await Assert.That(deeper.HadErrors).IsTrue();

		var withArguments = new CallState(text, 2, [text], CallState.OwnMessage);
		var bound = withArguments.AsValue(text, 2);
		await Assert.That(ReferenceEquals(bound, withArguments)).IsFalse();
		await Assert.That(bound.Arguments).IsNull();

		// A copy with other text still evaluates to the text it was built with, so it is not a plain value.
		var rewritten = plain with { Message = MarkupText.Plain("y") };
		var rebound = rewritten.AsValue(rewritten.Message, 2);
		await Assert.That(ReferenceEquals(rebound, rewritten)).IsFalse();
		await Assert.That((await rebound.ParsedMessage())?.ToPlainText()).IsEqualTo("y");
	}

	/// <summary>A state built from no message evaluates to none, though its text reads as empty.</summary>
	[Test]
	public async ValueTask NullMessage_EvaluatesToNull()
	{
		var state = new CallState((MarkupText?)null);
		await Assert.That(state.Message.ToPlainText()).IsEqualTo("");
		await Assert.That(await state.ParsedMessage()).IsNull();
	}

	/// <summary>
	/// Verifies that CallState constructed with an MString sets both
	/// <c>Message</c> and <c>ParsedMessage</c> to the same value.
	/// </summary>
	[Test]
	public async ValueTask NewCallState_MessageAndParsedMessageAreConsistent()
	{
		var msg = MarkupText.Plain("hello world");
		var state = new CallState(msg);

		await Assert.That(state.Message.ToPlainText()).IsEqualTo("hello world");
		var parsed = await state.ParsedMessage();
		await Assert.That(parsed?.ToPlainText()).IsEqualTo("hello world");
	}
}
