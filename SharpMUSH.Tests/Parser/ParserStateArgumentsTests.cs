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
		var ordered = (ParserState.Empty with { ArgumentList = ArgumentList.From(Bound(keys)) }).ArgumentsOrdered;

		await Assert.That(ordered.Keys).IsEquivalentTo(keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ordered.Values.Select(value => value.Message.ToPlainText()))
			.IsEquivalentTo(keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask NamedAndMissingArgumentsAreSortedOut()
	{
		var ordered = (ParserState.Empty with { ArgumentList = ArgumentList.From(Bound("name", "10", "2", "0")) }).ArgumentsOrdered;

		await Assert.That(ordered.Keys).IsEquivalentTo(["0", "2", "10"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask TheOrderedViewFollowsANewBinding()
	{
		var state = ParserState.Empty with { ArgumentList = ArgumentList.From(Bound("0", "1")) };
		await Assert.That(state.ArgumentsOrdered.Count).IsEqualTo(2);

		var rebound = state with { ArgumentList = ArgumentList.From(Bound("0")) };
		await Assert.That(rebound.ArgumentsOrdered.Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask ArgumentKeysAreSharedAndExact()
	{
		// Two lookups of the same position hand back the one shared string, not two equal ones.
		var first = ParserState.ArgumentKey(3);
		var second = ParserState.ArgumentKey(3);
		await Assert.That(second).IsSameReferenceAs(first);
		await Assert.That(ParserState.ArgumentKey(63)).IsEqualTo("63");
		await Assert.That(ParserState.ArgumentKey(64)).IsEqualTo("64");
		await Assert.That(ParserState.ArgumentKey(1000)).IsEqualTo("1000");
	}

	[Test]
	public async ValueTask KeysAreReadAsNumbers()
	{
		var ordered = (ParserState.Empty with { ArgumentList = ArgumentList.From(Bound("0", "1 ", "name")) }).ArgumentsOrdered;

		await Assert.That(ordered.Count).IsEqualTo(2);
		await Assert.That(ordered["1"].Message.ToPlainText()).IsEqualTo("1 ");
		await Assert.That(ordered.ContainsKey("2")).IsFalse();
		await Assert.That(ordered.ContainsKey("name")).IsFalse();
		await Assert.That(() => ordered["5"]).Throws<KeyNotFoundException>();
	}

	[Test]
	public async ValueTask EnumeratesKeysAndValuesTogether()
	{
		var ordered = (ParserState.Empty with { ArgumentList = ArgumentList.From(Bound("1", "0")) }).ArgumentsOrdered;

		await Assert.That(ordered.Select(pair => $"{pair.Key}={pair.Value.Message.ToPlainText()}"))
			.IsEquivalentTo(["0=0", "1=1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(ordered.Skip(1).Single().Key).IsEqualTo("1");
	}

	private static ArgumentList Positional(params string[] values)
		=> ArgumentList.Of([.. values.Select(value => new CallState(value))]);

	[Test]
	public async ValueTask APositionalListReadsAsTheDictionaryWould()
	{
		var list = Positional("a", "b", "c");
		var dictionary = list.AsDictionary();

		await Assert.That(list.IsPositional).IsTrue();
		await Assert.That(list.Count).IsEqualTo(dictionary.Count);
		await Assert.That(list.Keys).IsEquivalentTo(dictionary.Keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		foreach (var key in new[] { "0", "1", "2", "3", "00", "01", " 1", "1 ", "+1", "-1", "name", "", "99999999999" })
		{
			await Assert.That(list.ContainsKey(key)).IsEqualTo(dictionary.ContainsKey(key));
		}

		await Assert.That(list[1].Message.ToPlainText()).IsEqualTo("b");
		await Assert.That(list["2"].Message.ToPlainText()).IsEqualTo("c");
		await Assert.That(list.TryGet(3, out _)).IsFalse();
		await Assert.That(list.TryGet(-1, out _)).IsFalse();
		await Assert.That(list.GetValueOrDefault(5)).IsNull();
		await Assert.That(() => list[3]).Throws<KeyNotFoundException>();
	}

	[Test]
	public async ValueTask TheDictionaryIsBuiltOnceAndKept()
	{
		var state = ParserState.Empty with { ArgumentList = Positional("a") };

		await Assert.That(state.ArgumentList).IsSameReferenceAs(state.ArgumentList);
		await Assert.That((state with { Function = "x" }).ArgumentList).IsSameReferenceAs(state.ArgumentList);
	}

	[Test]
	public async ValueTask SettingTheDictionaryKeepsThatInstance()
	{
		var bound = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase)
		{
			["0"] = new("a"),
			["Name"] = new("b")
		};
		var state = ParserState.Empty with { ArgumentList = ArgumentList.From(bound) };

		await Assert.That(state.ArgumentList).IsSameReferenceAs(bound);
		await Assert.That(state.ArgumentList.IsPositional).IsFalse();
		await Assert.That(state.ArgumentList.Count).IsEqualTo(2);
		await Assert.That(state.ArgumentList["name"].Message.ToPlainText()).IsEqualTo("b");
		await Assert.That(state.ArgumentList[0].Message.ToPlainText()).IsEqualTo("a");
		await Assert.That(state.ArgumentsOrdered.Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask APositionalListOrdersWithoutADictionary()
	{
		var list = Positional("a", "b");
		var ordered = (ParserState.Empty with { ArgumentList = list }).ArgumentsOrdered;

		await Assert.That(ordered.Values.Select(value => value.Message.ToPlainText()))
			.IsEquivalentTo(["a", "b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(list.Ordered).IsSameReferenceAs(ordered);
	}

	[Test]
	public async ValueTask SelectKeepsTheForm()
	{
		var mapped = Positional("a", "b").Select(value => new CallState(value.Message.ToPlainText().ToUpperInvariant()));
		await Assert.That(mapped.IsPositional).IsTrue();
		await Assert.That(mapped[1].Message.ToPlainText()).IsEqualTo("B");

		var named = ArgumentList.From(new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase) { ["X"] = new("a") })
			.Select(value => value);
		await Assert.That(named.ContainsKey("x")).IsTrue();
	}

	[Test]
	public async ValueTask SuppliedKeysOfAPositionalListAreItsPositions()
	{
		var keys = AttributeArgumentEnvironment.SuppliedKeys(Positional("a", "b"));
		await Assert.That(keys.OrderBy(key => key)).IsEquivalentTo(["0", "1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask WithReplacesOnePositionAndKeepsTheRest()
	{
		var list = Positional("a", "b");
		var changed = list.With(0, new CallState("z"));
		await Assert.That(changed.IsPositional).IsTrue();
		await Assert.That(changed[0].Message.ToPlainText()).IsEqualTo("z");
		await Assert.That(changed[1].Message.ToPlainText()).IsEqualTo("b");
		await Assert.That(list[0].Message.ToPlainText()).IsEqualTo("a");
		await Assert.That(list.With(2, new CallState("c")).Count).IsEqualTo(3);

		var named = ArgumentList.From(new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase) { ["who"] = new("x") })
			.With(0, new CallState("first"));
		await Assert.That(named.ContainsKey("WHO")).IsTrue();
		await Assert.That(named[0].Message.ToPlainText()).IsEqualTo("first");
	}

	[Test]
	public async ValueTask AnAttributeFrameBindsArgumentsEnvironmentAndSuppliedKeysTogether()
	{
		var arguments = Positional("a");
		var frame = ParserState.Empty.ForAttributeArguments(arguments);
		await Assert.That(frame.ArgumentList).IsSameReferenceAs(arguments);
		await Assert.That(frame.EnvironmentRegisters["0"].Message.ToPlainText()).IsEqualTo("a");
		await Assert.That(frame.SuppliedArgumentKeys!).IsEquivalentTo(["0"]);

		var environment = new Dictionary<string, CallState> { ["0"] = new("a"), ["outer"] = new("kept") };
		var merged = ParserState.Empty.ForAttributeArguments(arguments, environment);
		await Assert.That(merged.EnvironmentRegisters).IsSameReferenceAs(environment);
		await Assert.That(merged.SuppliedArgumentKeys!).IsEquivalentTo(["0"]);
	}

	[Test]
	public async ValueTask ACommandFrameNamesTheCommandAndLeavesNoFunction()
	{
		var frame = ParserState.Empty.ForFunction("add", ArgumentList.Empty).ForCommand("GOTO", ["QUIET"], Positional("north"));
		await Assert.That(frame.Command).IsEqualTo("GOTO");
		await Assert.That(frame.Switches).IsEquivalentTo(["QUIET"]);
		await Assert.That(frame.ArgumentList[0].Message.ToPlainText()).IsEqualTo("north");
		await Assert.That(frame.Function).IsNull();
	}
}
