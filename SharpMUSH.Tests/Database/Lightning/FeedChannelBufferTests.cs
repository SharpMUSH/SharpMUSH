using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Channel recall kept as feed lines (<see cref="FeedChannelBufferService"/>) against the Lightning provider:
/// what a line keeps, ids, the size limit, a rename's merge, and that it outlives the process.
/// </summary>
public class FeedChannelBufferTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	private string _path = null!;
	private LightningDatabase _db = null!;
	private IChannelMessageIdSource _ids = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
		_ids = Substitute.For<IChannelMessageIdSource>();
		_ids.NextAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(77L));
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, as the other Lightning fixtures: a lingering mdb.lck can outlive the writer.
			}
		}
	}

	private FeedChannelBufferService Buffer() => new(_db, _ids);

	private static SharpChannelMessage Line(string channel, long id, bool seeAll = false) => new()
	{
		Id = id,
		ChannelId = channel,
		Timestamp = At.AddSeconds(id),
		Sender = new DBRef(1, 1700000000000),
		Message = MarkupText.Plain($"<Public> Ann says, \"line {id}\""),
		SeeAllOnly = seeAll,
		Style = "say",
		SpeakerName = "Ann",
		MessageText = $"line {id}"
	};

	[Test]
	public async Task ALine_ComesBackAsItWasDelivered()
	{
		var buffer = Buffer();
		await buffer.AddMessageAsync(Line("Channel/Public", 5, seeAll: true));

		var line = (await buffer.GetMessagesAsync("Channel/Public", 10).ToListAsync()).Single();
		await Assert.That(line.Message.ToPlainText()).IsEqualTo("<Public> Ann says, \"line 5\"");
		await Assert.That(line.MessageText).IsEqualTo("line 5");
		await Assert.That(line.SpeakerName).IsEqualTo("Ann");
		await Assert.That(line.Style).IsEqualTo("say");
		await Assert.That(line.SeeAllOnly).IsTrue();
		await Assert.That(line.Sender).IsEqualTo(new DBRef(1, 1700000000000));
		await Assert.That(line.Timestamp).IsEqualTo(At.AddSeconds(5));
		await Assert.That(await buffer.CountMessagesAsync("Channel/Public")).IsEqualTo(1);
	}

	[Test]
	public async Task ALineWithoutAnId_GetsTheNextOne_AndAGivenOneIsKept()
	{
		var buffer = Buffer();
		var unnumbered = new SharpChannelMessage
		{
			ChannelId = "Channel/Public",
			Timestamp = At,
			Sender = new DBRef(1),
			Message = MarkupText.Plain("written straight in")
		};
		await buffer.AddMessageAsync(unnumbered);
		await buffer.AddMessageAsync(Line("Channel/Public", 90));

		var lines = await buffer.GetMessagesAsync("Channel/Public", 10).ToListAsync();
		await Assert.That(lines.Select(line => line.Id)).IsEquivalentTo(new long[] { 77, 90 },
			TUnit.Assertions.Enums.CollectionOrdering.Matching).Because("cbufferadd() writes straight to the buffer, and its line needs an id too");
		await Assert.That(lines[0].Message.ToPlainText()).IsEqualTo("written straight in");
	}

	[Test]
	public async Task ABuffer_KeepsTheNewestHundred_AndOutlivesTheProcess()
	{
		var path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		try
		{
			await using (var first = Create(path))
			{
				await first.Migrate();
				var buffer = new FeedChannelBufferService(first, _ids);
				for (var id = 1; id <= 105; id++) await buffer.AddMessageAsync(Line("Channel/Public", id));
			}

			await using var second = Create(path);
			var lines = await new FeedChannelBufferService(second, _ids).GetMessagesAsync("Channel/Public", int.MaxValue).ToListAsync();
			await Assert.That(lines.Count).IsEqualTo(FeedChannelBufferService.BufferSize);
			await Assert.That(lines[0].Id).IsEqualTo(6);
			await Assert.That(lines[^1].Id).IsEqualTo(105);
		}
		finally
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, as in Cleanup.
			}
		}
	}

	/// <summary>
	/// A rename moves the buffer to the new id. A line that reached a buffer already under the new id (a
	/// broadcast in the moment between the rename and the move) is kept, in id order, not overwritten.
	/// </summary>
	[Test]
	public async Task MovingABuffer_MergesWithOneAlreadyAtTheDestination()
	{
		var buffer = Buffer();
		await buffer.AddMessageAsync(Line("Channel/Old", 1));
		await buffer.AddMessageAsync(Line("Channel/Old", 3));
		await buffer.AddMessageAsync(Line("Channel/New", 2));
		await buffer.AddMessageAsync(Line("Channel/New", 4));

		await buffer.MoveBufferAsync("Channel/Old", "Channel/New");

		var lines = await buffer.GetMessagesAsync("Channel/New", 10).ToListAsync();
		await Assert.That(lines.Select(line => line.Id)).IsEquivalentTo(new long[] { 1, 2, 3, 4 },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lines.All(line => line.ChannelId == "Channel/New")).IsTrue();
		await Assert.That(await buffer.CountMessagesAsync("Channel/Old")).IsEqualTo(0);
		await Assert.That(await buffer.CountMessagesAsync("Channel/New")).IsEqualTo(4);
		await Assert.That((await _db.GetFeedMessageAsync(1)).Expect<SharpFeedMessage>().Key).IsEqualTo("Channel/New");
	}

	[Test]
	public async Task ClearingABuffer_TakesItsLines()
	{
		var buffer = Buffer();
		await buffer.AddMessageAsync(Line("Channel/Gone", 1));
		await buffer.ClearBufferAsync("Channel/Gone");

		await Assert.That(await buffer.CountMessagesAsync("Channel/Gone")).IsEqualTo(0);
		await Assert.That(await buffer.GetMessagesAsync("Channel/Gone", 10).ToListAsync()).IsEmpty();
	}
}
