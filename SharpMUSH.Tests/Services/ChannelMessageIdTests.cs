using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The id a channel line carries in the recall buffer and in the <c>CHANNEL`MESSAGE</c> event, so the
/// portal can drop a pushed line it already pulled. Read markers persist these ids, and the buffer does
/// not survive a restart, so they must keep rising across one: they are taken from the clock.
/// </summary>
public class ChannelMessageIdTests
{
	private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public async Task AnId_IsTheMicrosecondItWasTaken()
	{
		var ids = new ChannelMessageIdSource(new StoppedClock(At));

		await Assert.That(ids.Next()).IsEqualTo(At.ToUnixTimeMilliseconds() * 1000);
	}

	[Test]
	public async Task Ids_KeepRising_WhenTheClockDoesNot()
	{
		var clock = new StoppedClock(At);
		var ids = new ChannelMessageIdSource(clock);

		var first = ids.Next();
		var second = ids.Next();
		clock.Now = At.AddSeconds(-5);
		var third = ids.Next();

		await Assert.That(second).IsGreaterThan(first);
		await Assert.That(third).IsGreaterThan(second).Because("a clock stepped back must not reuse an id");
	}

	[Test]
	public async Task Ids_AreUnique_UnderConcurrentUse()
	{
		var ids = new ChannelMessageIdSource(new StoppedClock(At));

		var taken = await Task.WhenAll(Enumerable.Range(0, 64)
			.Select(_ => Task.Run(() => Enumerable.Range(0, 500).Select(_ => ids.Next()).ToArray())));

		var all = taken.SelectMany(x => x).ToArray();
		await Assert.That(all.Distinct().Count()).IsEqualTo(all.Length);
	}

	[Test]
	public async Task TheBuffer_GivesALineWithoutAnIdTheNextOne_AndKeepsAGivenOne()
	{
		var ids = new ChannelMessageIdSource(new StoppedClock(At));
		var buffer = new InMemoryChannelBufferService(ids);
		var channel = Guid.NewGuid().ToString("N");

		await buffer.AddMessageAsync(Line(channel, id: 0));
		await buffer.AddMessageAsync(Line(channel, id: 42));

		var lines = await buffer.GetMessagesAsync(channel, 10).ToListAsync();
		await Assert.That(lines[0].Id).IsEqualTo(At.ToUnixTimeMilliseconds() * 1000)
			.Because("cbufferadd() writes straight to the buffer, and its line needs an id too");
		await Assert.That(lines[1].Id).IsEqualTo(42);
	}

	private static SharpChannelMessage Line(string channel, long id) => new()
	{
		Id = id,
		ChannelId = channel,
		Timestamp = At,
		Sender = new DBRef(1),
		Message = MarkupText.Plain("hello"),
		MessageType = "Say"
	};
}
