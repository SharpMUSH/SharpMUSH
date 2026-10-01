using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The id a channel line carries in the recall buffer and in the <c>CHANNEL`MESSAGE</c> event, so the
/// portal can drop a pushed line it already pulled. Read markers persist these ids, and the buffer does
/// not survive a restart, so they must keep rising across one — even when the clock does not.
/// </summary>
public class ChannelMessageIdTests
{
	private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>Server data kept in memory, standing in for the database across a "restart".</summary>
	private sealed class ServerData : IExpandedObjectDataService
	{
		private readonly Dictionary<string, object> _data = [];
		public int Writes { get; private set; }

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class =>
			ValueTask.FromResult(_data.TryGetValue(typeof(T).Name, out var value) ? (T?)value : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			Writes++;
			_data[typeof(T).Name] = data;
			return ValueTask.CompletedTask;
		}

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();
		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class =>
			throw new NotSupportedException();
	}

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	private static long Micros(DateTimeOffset at) => at.ToUnixTimeMilliseconds() * 1000;

	[Test]
	public async Task AnId_IsTheMicrosecondItWasTaken()
	{
		var ids = new ChannelMessageIdSource(new ServerData(), new StoppedClock(At));

		await Assert.That(await ids.NextAsync()).IsEqualTo(Micros(At));
		await Assert.That(ids.Latest).IsEqualTo(Micros(At));
	}

	[Test]
	public async Task Ids_KeepRising_WhenTheClockDoesNot()
	{
		var clock = new StoppedClock(At);
		var ids = new ChannelMessageIdSource(new ServerData(), clock);

		var first = await ids.NextAsync();
		var second = await ids.NextAsync();
		clock.Now = At.AddSeconds(-5);
		var third = await ids.NextAsync();

		await Assert.That(second).IsGreaterThan(first);
		await Assert.That(third).IsGreaterThan(second).Because("a clock stepped back must not reuse an id");
	}

	/// <summary>
	/// The bound on a read marker's id after a restart: every id the old process handed out, which can be
	/// ahead of this process's clock, and nothing this process hands out next.
	/// </summary>
	[Test]
	public async Task TheCeiling_AfterARestart_CoversEveryIdTheOldProcessIssued_BeforeAnyNewOne()
	{
		var data = new ServerData();
		var before = new ChannelMessageIdSource(data, new StoppedClock(At));
		var issued = await before.NextAsync();

		// A new process whose clock is behind, asked for the ceiling before it has issued anything: a
		// marker on the old process's id must not be cut down to the clock.
		var after = new ChannelMessageIdSource(data, new StoppedClock(At.AddMinutes(-10)));

		var ceiling = await after.CeilingAsync();
		await Assert.That(ceiling).IsGreaterThanOrEqualTo(issued);
		await Assert.That(await after.NextAsync()).IsGreaterThan(ceiling)
			.Because("a marker at the ceiling must not call a line issued after it read");
	}

	/// <summary>
	/// The clock going back across a restart: the new process must not hand out ids below those the old
	/// one did, or a persisted read marker calls every new line read.
	/// </summary>
	[Test]
	public async Task Ids_KeepRising_AcrossARestartWithTheClockSetBack()
	{
		var data = new ServerData();
		var before = new ChannelMessageIdSource(data, new StoppedClock(At));
		var issued = new List<long>();
		for (var i = 0; i < 5; i++)
		{
			issued.Add(await before.NextAsync());
		}

		var after = new ChannelMessageIdSource(data, new StoppedClock(At.AddHours(-1)));

		await Assert.That(await after.NextAsync()).IsGreaterThan(issued.Max());
	}

	/// <summary>The high-water mark is written ahead in blocks, not once per line.</summary>
	[Test]
	public async Task TheHighWaterMark_IsWrittenOncePerBlock()
	{
		var data = new ServerData();
		var clock = new StoppedClock(At);
		var ids = new ChannelMessageIdSource(data, clock);

		for (var i = 0; i < 100; i++)
		{
			clock.Now = At.AddMilliseconds(i * 100.0);
			await ids.NextAsync();
		}

		await Assert.That(data.Writes).IsEqualTo(1).Because("ten seconds of lines fit in one reserved block");
	}

	[Test]
	public async Task Ids_AreUnique_UnderConcurrentUse()
	{
		var ids = new ChannelMessageIdSource(new ServerData(), new StoppedClock(At));

		var taken = await Task.WhenAll(Enumerable.Range(0, 64)
			.Select(_ => Task.Run(async () =>
			{
				var mine = new long[200];
				for (var i = 0; i < mine.Length; i++) mine[i] = await ids.NextAsync();
				return mine;
			})));

		var all = taken.SelectMany(x => x).ToArray();
		await Assert.That(all.Distinct().Count()).IsEqualTo(all.Length);
	}

	[Test]
	public async Task TheBuffer_GivesALineWithoutAnIdTheNextOne_AndKeepsAGivenOne()
	{
		var ids = new ChannelMessageIdSource(new ServerData(), new StoppedClock(At));
		var buffer = new InMemoryChannelBufferService(ids);
		var channel = Guid.NewGuid().ToString("N");

		await buffer.AddMessageAsync(Line(channel, id: 0));
		await buffer.AddMessageAsync(Line(channel, id: 42));

		var lines = await buffer.GetMessagesAsync(channel, 10).ToListAsync();
		await Assert.That(lines[0].Id).IsEqualTo(Micros(At))
			.Because("cbufferadd() writes straight to the buffer, and its line needs an id too");
		await Assert.That(lines[1].Id).IsEqualTo(42);
	}

	/// <summary>
	/// A rename moves the buffer to the new id. A line that reached a buffer already under the new id
	/// (a broadcast in the moment between the rename and the move) is kept, in id order, not overwritten.
	/// </summary>
	[Test]
	public async Task MovingABuffer_MergesWithOneAlreadyAtTheDestination()
	{
		var buffer = new InMemoryChannelBufferService(new ChannelMessageIdSource(new ServerData(), new StoppedClock(At)));
		var from = Guid.NewGuid().ToString("N");
		var to = Guid.NewGuid().ToString("N");
		await buffer.AddMessageAsync(Line(from, id: 1));
		await buffer.AddMessageAsync(Line(from, id: 3));
		await buffer.AddMessageAsync(Line(to, id: 2));
		await buffer.AddMessageAsync(Line(to, id: 4));

		await buffer.MoveBufferAsync(from, to);

		var lines = await buffer.GetMessagesAsync(to, 10).ToListAsync();
		await Assert.That(lines.Select(line => line.Id)).IsEquivalentTo(new long[] { 1, 2, 3, 4 },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lines.All(line => line.ChannelId == to)).IsTrue();
		await Assert.That(await buffer.CountMessagesAsync(from)).IsEqualTo(0);
	}

	[Test]
	public async Task AMergedBuffer_KeepsTheNewestHundred()
	{
		var buffer = new InMemoryChannelBufferService(new ChannelMessageIdSource(new ServerData(), new StoppedClock(At)));
		var from = Guid.NewGuid().ToString("N");
		var to = Guid.NewGuid().ToString("N");
		for (var i = 1; i <= 80; i++)
		{
			await buffer.AddMessageAsync(Line(from, id: i * 2));
			await buffer.AddMessageAsync(Line(to, id: i * 2 + 1));
		}

		await buffer.MoveBufferAsync(from, to);

		var lines = await buffer.GetMessagesAsync(to, int.MaxValue).ToListAsync();
		await Assert.That(lines.Count).IsEqualTo(100);
		await Assert.That(lines[0].Id).IsEqualTo(62);
		await Assert.That(lines[^1].Id).IsEqualTo(161);
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
