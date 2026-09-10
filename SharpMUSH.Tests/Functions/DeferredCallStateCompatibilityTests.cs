using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DeferredCallStateCompatibilityTests
{
	[Test]
	[Arguments(true, false, false)]
	[Arguments(true, false, true)]
	[Arguments(false, false, false)]
	[Arguments(false, true, false)]
	[Arguments(true, true, false)]
	public async Task FullDelegateRetainsBothExistingAndEvaluatedFailures(bool existing, bool evaluated, bool empty)
	{
		var calls = 0;
		var state = new CallState("raw")
		{
			HadErrors = existing,
			ParsedResult = () =>
			{
				calls++;
				return ValueTask.FromResult<CallState?>(empty ? null : new CallState("parsed") { HadErrors = evaluated });
			}
		};
		var result = await state.GetParsedResultAsync();
		await Assert.That(result.HadErrors).IsEqualTo(existing || evaluated);
		await Assert.That(result.Message!.Text).IsEqualTo(empty ? "" : "parsed");
		await Assert.That(calls).IsEqualTo(1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublishedConstructorAndTextDelegateRemainCompatible(bool existing)
	{
		var calls = 0;
		var state = new CallState(MarkupText.Plain("raw"), 7, null, () =>
		{
			calls++;
			return ValueTask.FromResult<MarkupText?>(MarkupText.Plain("parsed"));
		}, true)
		{ HadErrors = existing };
		var (raw, depth, arguments, legacy, preserveSpaces) = state;
		await Assert.That(raw!.Text).IsEqualTo("raw");
		await Assert.That(depth).IsEqualTo(7);
		await Assert.That(arguments).IsNull();
		await Assert.That(preserveSpaces).IsTrue();
		await Assert.That((await legacy())!.Text).IsEqualTo("parsed");
		var result = await state.GetParsedResultAsync();
		await Assert.That(result.Message!.Text).IsEqualTo("parsed");
		await Assert.That(result.HadErrors).IsEqualTo(existing);
		await Assert.That(calls).IsEqualTo(2);
	}
}
