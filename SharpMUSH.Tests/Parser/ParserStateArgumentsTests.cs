using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// <see cref="ParserState.ArgumentsOrdered"/> builds the usual <c>"0"</c>…<c>"n-1"</c> binding directly and
/// sorts anything else; both must give the numbered arguments in numeric order and nothing more.
/// </summary>
public class ParserStateArgumentsTests
{
	private static Dictionary<string, CallState> Bound(params string[] keys)
		=> keys.ToDictionary(key => key, key => new CallState(key));

	[Test]
	public async ValueTask ContiguousArgumentsKeepNumericOrder()
	{
		var keys = Enumerable.Range(0, 12).Select(ParserState.ArgumentKey).ToArray();
		var ordered = (ParserState.Empty with { Arguments = Bound(keys) }).ArgumentsOrdered;

		await Assert.That(ordered.Keys).IsEquivalentTo(keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ordered.Values.Select(value => value.Message.ToPlainText()))
			.IsEquivalentTo(keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask NamedAndMissingArgumentsAreSortedOut()
	{
		var ordered = (ParserState.Empty with { Arguments = Bound("name", "10", "2", "0") }).ArgumentsOrdered;

		await Assert.That(ordered.Keys).IsEquivalentTo(["0", "2", "10"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask TheOrderedViewFollowsANewBinding()
	{
		var state = ParserState.Empty with { Arguments = Bound("0", "1") };
		await Assert.That(state.ArgumentsOrdered.Count).IsEqualTo(2);

		var rebound = state with { Arguments = Bound("0") };
		await Assert.That(rebound.ArgumentsOrdered.Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask ArgumentKeysAreSharedAndExact()
	{
		await Assert.That(ReferenceEquals(ParserState.ArgumentKey(3), ParserState.ArgumentKey(3))).IsTrue();
		await Assert.That(ParserState.ArgumentKey(63)).IsEqualTo("63");
		await Assert.That(ParserState.ArgumentKey(64)).IsEqualTo("64");
		await Assert.That(ParserState.ArgumentKey(1000)).IsEqualTo("1000");
	}

	[Test]
	public async ValueTask KeysAreReadAsNumbers()
	{
		var ordered = (ParserState.Empty with { Arguments = Bound("0", "1 ", "name") }).ArgumentsOrdered;

		await Assert.That(ordered.Count).IsEqualTo(2);
		await Assert.That(ordered["1"].Message.ToPlainText()).IsEqualTo("1 ");
		await Assert.That(ordered.ContainsKey("2")).IsFalse();
		await Assert.That(ordered.ContainsKey("name")).IsFalse();
		await Assert.That(() => ordered["5"]).Throws<KeyNotFoundException>();
	}

	[Test]
	public async ValueTask EnumeratesKeysAndValuesTogether()
	{
		var ordered = (ParserState.Empty with { Arguments = Bound("1", "0") }).ArgumentsOrdered;

		await Assert.That(ordered.Select(pair => $"{pair.Key}={pair.Value.Message.ToPlainText()}"))
			.IsEquivalentTo(["0=0", "1=1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ordered.Skip(1).Single().Key).IsEqualTo("1");
	}
}
