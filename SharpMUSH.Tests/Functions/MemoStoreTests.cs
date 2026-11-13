using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>The memory and the window memo() answers are kept within, on a clock of the test's own.</summary>
public class MemoStoreTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed record Options(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	private static MemoStore Store(Clock clock, uint maxBytes = LimitOptions.DefaultMemoMaxBytes,
		uint window = LimitOptions.DefaultMemoWindow, uint maxEntries = LimitOptions.DefaultMemoMaxEntries)
	{
		var defaults = SharpMUSHOptions.Default();
		return new MemoStore(new Options(defaults with
		{
			Limit = defaults.Limit with { MemoMaxBytes = maxBytes, MemoWindow = window, MemoMaxEntries = maxEntries }
		}), clock);
	}

	private static MemoEntry Answer(string text) => new(MarkupText.Plain(text), []);

	[Test]
	public async Task AKeptAnswerIsFound()
	{
		var store = Store(new Clock());
		store.Keep("k", Answer("42"));

		await Assert.That(store.Find("k").Expect<MemoEntry>().Value.ToPlainText()).IsEqualTo("42");
	}

	[Test]
	public async Task TheWindowEndsAnAnswer()
	{
		var clock = new Clock();
		var store = Store(clock, window: 60);
		store.Keep("k", Answer("42"));

		clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(59);
		store.Find("k").Expect<MemoEntry>();

		clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(60);
		store.Find("k").Expect<NotFound>();
		await Assert.That(store.Usage).IsEqualTo((0, 0L));
	}

	[Test]
	public async Task AWindowOfZeroKeepsAnAnswerUntilItIsPushedOut()
	{
		var clock = new Clock();
		var store = Store(clock, window: 0);
		store.Keep("k", Answer("42"));

		clock.Now = DateTimeOffset.UnixEpoch.AddDays(365);
		await Assert.That(store.Find("k").Expect<MemoEntry>().Value.ToPlainText()).IsEqualTo("42");
	}

	[Test]
	public async Task TheLeastRecentlyUsedAnswerGoesFirst()
	{
		var probe = Store(new Clock());
		probe.Keep("a", Answer(new string('x', 100)));
		var one = probe.Usage.Bytes;

		var store = Store(new Clock(), maxBytes: (uint)(one * 2));
		store.Keep("a", Answer(new string('x', 100)));
		store.Keep("b", Answer(new string('y', 100)));
		store.Find("a").Expect<MemoEntry>();
		store.Keep("c", Answer(new string('z', 100)));

		store.Find("a").Expect<MemoEntry>();
		store.Find("b").Expect<NotFound>();
		store.Find("c").Expect<MemoEntry>();
		await Assert.That(store.Usage.Bytes).IsLessThanOrEqualTo(one * 2);
	}

	[Test]
	public async Task TheCountPushesOutTheLeastRecentlyUsed()
	{
		var store = Store(new Clock(), maxEntries: 2);
		store.Keep("a", Answer("1"));
		store.Keep("b", Answer("2"));
		store.Find("a").Expect<MemoEntry>();
		store.Keep("c", Answer("3"));

		store.Find("b").Expect<NotFound>();
		store.Find("a").Expect<MemoEntry>();
		store.Find("c").Expect<MemoEntry>();
		await Assert.That(store.Usage.Count).IsEqualTo(2);
	}

	[Test]
	public async Task ACountOfZeroKeepsNothing()
	{
		var store = Store(new Clock(), maxEntries: 0);
		store.Keep("a", Answer("1"));

		store.Find("a").Expect<NotFound>();
		await Assert.That(store.Usage.Count).IsEqualTo(0);
	}

	[Test]
	public async Task AnAnswerLargerThanTheBudgetIsNotKept()
	{
		var store = Store(new Clock(), maxBytes: 64);
		store.Keep("k", Answer(new string('x', 1000)));

		store.Find("k").Expect<NotFound>();
		await Assert.That(store.Usage.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ReadsCountTowardTheBudget()
	{
		var store = Store(new Clock());
		store.Keep("plain", Answer("x"));
		var plain = store.Usage.Bytes;
		store.Forget("plain");

		store.Keep("reads", new MemoEntry(MarkupText.Plain("x"),
			[new AttributeReadDependency(new(1), new(2), "DATA", IAttributeService.AttributeMode.Read, true, "P" + new string('v', 500))]));

		await Assert.That(store.Usage.Bytes - plain).IsGreaterThanOrEqualTo(1000L);
	}
}
