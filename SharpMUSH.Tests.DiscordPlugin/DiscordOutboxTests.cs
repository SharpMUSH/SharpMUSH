using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordOutboxTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	/// <summary>NextDouble is always one half, so a back-off is half its ceiling.</summary>
	private sealed class HalfRandom : Random
	{
		protected override double Sample() => 0.5;
	}

	private static DiscordOutbox Outbox(DiscordOutboxOptions? options = null)
		=> new(options ?? new DiscordOutboxOptions(), new HalfRandom());

	private static OutboundLine Line(string speaker, string text, int second = 0)
		=> new(speaker, null, text, Start.AddSeconds(second));

	[Test]
	public async Task ABurstFromOneSpeaker_IsOnePost()
	{
		var outbox = Outbox();
		outbox.Enqueue(Line("Ann", "one"));
		outbox.Enqueue(Line("Ann", "two"));
		outbox.Enqueue(Line("Ann", "three"));

		await Assert.That(outbox.Next(Start)).IsEqualTo(new DiscordPost("Ann", null, "one\ntwo\nthree", 3));
	}

	[Test]
	public async Task EachSpeaker_GetsTheirOwnPost_InOrder()
	{
		var outbox = Outbox();
		outbox.Enqueue(Line("Ann", "one"));
		outbox.Enqueue(Line("Bo", "two"));
		outbox.Enqueue(Line("Ann", "three"));

		List<string> sent = [];
		while (outbox.Next(Start) is { } post)
		{
			sent.Add($"{post.Speaker}: {post.Content}");
			outbox.Report(new Posted(1), Start);
		}

		await Assert.That(sent).IsEquivalentTo(new[] { "Ann: one", "Bo: two", "Ann: three" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task APostStopsJoiningAtTheLimit()
	{
		var outbox = Outbox(new DiscordOutboxOptions(MaxContent: 10));
		outbox.Enqueue(Line("Ann", "12345"));
		outbox.Enqueue(Line("Ann", "6789"));
		outbox.Enqueue(Line("Ann", "x"));

		await Assert.That(outbox.Next(Start)!.Content).IsEqualTo("12345\n6789");
	}

	[Test]
	public async Task ALongLine_IsSplitAtSpaces_AndCut()
	{
		var parts = DiscordOutbox.Split("aaaa bbbb cccc dddd eeee ffff gggg hhhh iiii", 20, 2);

		await Assert.That(parts).IsEquivalentTo(new[] { "aaaa bbbb cccc dddd", "eeee ff… (truncated)" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ALongLine_GoesOutAsParts_BeforeLaterLines()
	{
		var outbox = Outbox(new DiscordOutboxOptions(MaxContent: 20));
		outbox.Enqueue(Line("Ann", "aaaa bbbb cccc dddd eeee ffff"));
		outbox.Enqueue(Line("Bo", "after"));

		List<string> sent = [];
		while (outbox.Next(Start) is { } post)
		{
			sent.Add(post.Content);
			outbox.Report(new Posted(1), Start);
		}

		await Assert.That(sent).IsEquivalentTo(new[] { "aaaa bbbb cccc dddd", "eeee ffff", "after" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Overflow_DropsTheOldest_AndOwesANotice()
	{
		var outbox = Outbox(new DiscordOutboxOptions(MaxLines: 2));
		outbox.Enqueue(Line("Ann", "one"));
		outbox.Enqueue(Line("Bo", "two"));
		outbox.Enqueue(Line("Cy", "three"));
		outbox.Enqueue(Line("Di", "four"));

		var notice = outbox.Next(Start)!;
		outbox.Report(new Posted(1), Start);
		var next = outbox.Next(Start)!;

		await Assert.That(notice.Content).IsEqualTo("[2 lines were not relayed.]");
		await Assert.That(next.Content).IsEqualTo("three");
		await Assert.That(outbox.NotRelayed).IsEqualTo(0);
	}

	[Test]
	public async Task StaleLines_AreDropped_NotSent()
	{
		var outbox = Outbox();
		outbox.Enqueue(Line("Ann", "old"));
		outbox.Enqueue(Line("Ann", "new", second: 50));

		var post = outbox.Next(Start.AddSeconds(70))!;

		await Assert.That(post.Content).IsEqualTo("[1 line was not relayed.]");
	}

	[Test]
	public async Task ATransientFailure_RetriesTheSamePost_WithGrowingWaits()
	{
		var outbox = Outbox();
		outbox.Enqueue(Line("Ann", "hello"));
		var post = outbox.Next(Start);

		List<TimeSpan> waits = [];
		for (var attempt = 0; attempt < 4; attempt++)
		{
			waits.Add(outbox.Report(new Transient("502"), Start));
			await Assert.That(outbox.Next(Start)).IsSameReferenceAs(post);
		}

		await Assert.That(waits.Select(wait => wait.TotalMilliseconds))
			.IsEquivalentTo(new[] { 500d, 1000d, 2000d, 4000d }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheWaitIsCapped()
	{
		var outbox = Outbox(new DiscordOutboxOptions(MaxAttempts: 10, BackoffCap: TimeSpan.FromSeconds(3)));
		outbox.Enqueue(Line("Ann", "hello"));
		outbox.Next(Start);

		var last = TimeSpan.Zero;
		for (var attempt = 0; attempt < 6; attempt++)
		{
			last = outbox.Report(new Transient("502"), Start);
		}

		await Assert.That(last).IsEqualTo(TimeSpan.FromSeconds(1.5));
	}

	[Test]
	public async Task AfterTheLastAttempt_ThePostIsDropped_AndOwed()
	{
		var outbox = Outbox(new DiscordOutboxOptions(MaxAttempts: 2));
		outbox.Enqueue(Line("Ann", "one"));
		outbox.Enqueue(Line("Ann", "two"));
		outbox.Next(Start);

		outbox.Report(new Transient("502"), Start);
		outbox.Report(new Transient("502"), Start);

		await Assert.That(outbox.Next(Start)!.Content).IsEqualTo("[2 lines were not relayed.]");
	}

	[Test]
	public async Task ABrokenLink_Stops_UntilResumed()
	{
		var outbox = Outbox();
		outbox.Enqueue(Line("Ann", "hello"));
		var post = outbox.Next(Start);

		outbox.Report(new LinkBroken(10015, "The channel's webhook was deleted or reset."), Start);

		await Assert.That(outbox.State).IsEqualTo(DiscordLinkState.Faulted);
		await Assert.That(outbox.Next(Start)).IsNull();
		outbox.Resume();
		await Assert.That(outbox.Next(Start)).IsSameReferenceAs(post);
	}

	[Test]
	public async Task OneRefusedMessage_IsSkipped_ButFiveInAMinuteFaultTheLink()
	{
		var outbox = Outbox();
		for (var line = 0; line < 6; line++)
		{
			outbox.Enqueue(Line($"Speaker{line}", "bad"));
		}

		for (var refused = 1; refused <= 5; refused++)
		{
			outbox.Next(Start);
			outbox.Report(new Rejected("Invalid Form Body"), Start.AddSeconds(refused));
			await Assert.That(outbox.State == DiscordLinkState.Faulted).IsEqualTo(refused == 5);
		}
	}

	[Test]
	public async Task ABacklog_IsReported()
	{
		var outbox = Outbox(new DiscordOutboxOptions(BacklogLines: 1));
		outbox.Enqueue(Line("Ann", "one"));
		outbox.Enqueue(Line("Bo", "two"));

		await Assert.That(outbox.State).IsEqualTo(DiscordLinkState.Backlogged);
	}
}
