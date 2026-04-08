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
		var first = new CallState(MModule.single("Boo! "));
		var second = new CallState(MModule.single("a"));

		var concatenated = MModule.concat(first.Message!, second.Message!);

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
	/// Verifies that CallState constructed with an MString sets both
	/// <c>Message</c> and <c>ParsedMessage</c> to the same value.
	/// </summary>
	[Test]
	public async ValueTask NewCallState_MessageAndParsedMessageAreConsistent()
	{
		var msg = MModule.single("hello world");
		var state = new CallState(msg);

		await Assert.That(state.Message?.ToPlainText()).IsEqualTo("hello world");
		var parsed = await state.ParsedMessage();
		await Assert.That(parsed?.ToPlainText()).IsEqualTo("hello world");
	}
}
