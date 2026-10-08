using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="IFeedStore"/> against the Lightning provider directly: kinds, feeds, members, lines kept within
/// their limits, taps, and the object-delete cascade.
/// </summary>
public class FeedStoreTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
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

	private static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

	private async Task<DBRef> NewPlayer(string name)
	{
		var created = await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0);
		return (await _db.GetObjectNodeAsync(created)).Expect<SharpPlayer>().Object.DBRef;
	}

	private static SharpFeedMessage Line(long id, DBRef speaker, string text, string key = "101.5", DateTimeOffset? at = null)
		=> new(id, "radio", key, at ?? At.AddSeconds(id), speaker, "Ann", speaker, "Ann", null, "", FeedStyles.Say, MarkupText.Plain(text),
			id == 4 ? "Ghost" : "");

	private static FeedSettings Keep(int messages = 0, long bytes = 0, TimeSpan? age = null) => new(messages, bytes, 0, age, true, "say");

	[Test]
	public async Task AKind_IsWrittenReadAndReplaced()
	{
		var owner = await NewPlayer("RadioOwner");
		var kind = new SharpFeedKind("radio", owner, "Short-wave", new FeedSettings(MaxMessages: 50, MaxAge: TimeSpan.FromDays(2)),
			new Dictionary<string, string> { ["read"] = "role^police", ["send"] = "LK`MEMBER/1" });
		await _db.SetFeedKindAsync(kind);

		var read = (await _db.GetFeedKindAsync("radio")).Expect<SharpFeedKind>();
		await Assert.That(read with { Locks = kind.Locks }).IsEqualTo(kind);
		await Assert.That(read.Locks).IsEquivalentTo(kind.Locks);
		await Assert.That(await _db.GetFeedKindAsync("text") is NotFound).IsTrue();

		await _db.SetFeedKindAsync(kind with { Description = "Radio" });
		var kinds = await _db.GetFeedKindsAsync();
		await Assert.That(kinds.Count).IsEqualTo(1);
		await Assert.That(kinds[0].Description).IsEqualTo("Radio");
	}

	[Test]
	public async Task Lines_AreKeptWithinMaxMessages_OldestGoingFirst()
	{
		var ann = await NewPlayer("Ann");
		for (var id = 1; id <= 5; id++) await _db.AppendFeedMessageAsync(Line(id, ann, $"line {id}"), Keep(messages: 3));

		var lines = await _db.GetFeedMessagesAsync("radio", "101.5", 0, 0);
		await Assert.That(lines.Select(line => line.Id)).IsEquivalentTo(new long[] { 3, 4, 5 });
		await Assert.That(lines.Select(line => line.Text.ToPlainText())).IsEquivalentTo(new[] { "line 3", "line 4", "line 5" });

		var feed = (await _db.GetFeedAsync("radio", "101.5")).Expect<SharpFeed>();
		await Assert.That(feed.Messages).IsEqualTo(3);
		await Assert.That(feed.LastId).IsEqualTo(5);
		await Assert.That(await _db.GetFeedMessageAsync(1) is NotFound).IsTrue();
		await Assert.That((await _db.GetFeedMessageAsync(4)).Expect<SharpFeedMessage>().Feed).IsEqualTo("radio/101.5");
		await Assert.That((await _db.GetFeedMessageAsync(4)).Expect<SharpFeedMessage>().DisplayName).IsEqualTo("Ghost");
	}

	[Test]
	public async Task Lines_AreKeptWithinMaxBytesAndMaxAge()
	{
		var ann = await NewPlayer("Ann");
		await _db.AppendFeedMessageAsync(Line(1, ann, "old", at: At), Keep(age: TimeSpan.FromHours(1)));
		await _db.AppendFeedMessageAsync(Line(2, ann, "new", at: At.AddHours(2)), Keep(age: TimeSpan.FromHours(1)));
		await Assert.That((await _db.GetFeedMessagesAsync("radio", "101.5", 0, 0)).Select(l => l.Id)).IsEquivalentTo(new long[] { 2 });

		var size = (await _db.GetFeedAsync("radio", "101.5")).Expect<SharpFeed>().Bytes;
		await _db.AppendFeedMessageAsync(Line(3, ann, "new", at: At.AddHours(2)), Keep(bytes: size));
		await Assert.That((await _db.GetFeedMessagesAsync("radio", "101.5", 0, 0)).Select(l => l.Id)).IsEquivalentTo(new long[] { 3 });
		await Assert.That((await _db.GetFeedAsync("radio", "101.5")).Expect<SharpFeed>().Bytes).IsEqualTo(size);
	}

	[Test]
	public async Task Recall_TakesTheNewestAfterAnId_OldestFirst()
	{
		var ann = await NewPlayer("Ann");
		for (var id = 1; id <= 6; id++) await _db.AppendFeedMessageAsync(Line(id, ann, $"line {id}"), Keep());
		await _db.AppendFeedMessageAsync(Line(7, ann, "elsewhere", key: "99.1"), Keep());

		await Assert.That((await _db.GetFeedMessagesAsync("radio", "101.5", 2, 0)).Select(l => l.Id)).IsEquivalentTo(new long[] { 5, 6 });
		await Assert.That((await _db.GetFeedMessagesAsync("radio", "101.5", 0, 4)).Select(l => l.Id)).IsEquivalentTo(new long[] { 5, 6 });
		await Assert.That((await _db.GetFeedsAsync("radio")).Select(f => f.Name)).IsEquivalentTo(new[] { "radio/101.5", "radio/99.1" });
	}

	[Test]
	public async Task Members_AreReadBothWays_AndEndWithTheirObject()
	{
		var owner = await NewPlayer("RadioOwner");
		var ann = await NewPlayer("Ann");
		var bo = await NewPlayer("Bo");
		await _db.SetFeedKindAsync(new SharpFeedKind("radio", owner, "", FeedSettings.None, FeedLocks.None));
		await _db.SetFeedMemberAsync("radio", "101.5", new SharpFeedMember(ann, 0, false, 0));
		await _db.SetFeedMemberAsync("radio", "101.5", new SharpFeedMember(bo, 0, true, 0));
		await _db.SetFeedMemberAsync("radio", "99.1", new SharpFeedMember(ann, 3, false, 4));

		var members = await _db.GetFeedMembersAsync("radio", "101.5");
		await Assert.That(members.Select(m => m.Member)).IsEquivalentTo(new[] { ann, bo });
		await Assert.That(members.Single(m => m.Member == bo).Gag).IsTrue();
		await Assert.That(await _db.GetMemberFeedsAsync(ann, "radio")).IsEquivalentTo(new[] { ("radio", "101.5"), ("radio", "99.1") });

		await _db.DeleteObjectAsync(ann);

		await Assert.That((await _db.GetFeedMembersAsync("radio", "101.5")).Select(m => m.Member)).IsEquivalentTo(new[] { bo });
		await Assert.That(await _db.GetFeedMembersAsync("radio", "99.1")).IsEmpty();
	}

	[Test]
	public async Task Taps_AreAddedOnceAndEndWithTheirObject()
	{
		var logger = await NewPlayer("Logger");
		var tap = new SharpFeedTap("radio", logger, "LOG`RADIO");
		await _db.AddFeedTapAsync(tap);
		await _db.AddFeedTapAsync(tap);
		await _db.AddFeedTapAsync(new SharpFeedTap("*", logger, "LOG`ALL"));

		await Assert.That(await _db.GetFeedTapsAsync("radio")).IsEquivalentTo(new[] { tap });
		await Assert.That((await _db.GetFeedTapsAsync(null)).Count).IsEqualTo(2);

		await _db.DeleteObjectAsync(logger);
		await Assert.That(await _db.GetFeedTapsAsync(null)).IsEmpty();
	}

	[Test]
	public async Task DeletingAKind_TakesItsFeedsMembersLinesAndTaps()
	{
		var owner = await NewPlayer("RadioOwner");
		var ann = await NewPlayer("Ann");
		await _db.SetFeedKindAsync(new SharpFeedKind("radio", owner, "", FeedSettings.None, FeedLocks.None));
		await _db.SetFeedMemberAsync("radio", "101.5", new SharpFeedMember(ann, 0, false, 0));
		await _db.AppendFeedMessageAsync(Line(1, ann, "hello"), Keep());
		await _db.AddFeedTapAsync(new SharpFeedTap("radio", owner, "LOG"));

		await Assert.That(await _db.DeleteFeedKindAsync("radio")).IsTrue();

		await Assert.That(await _db.GetFeedsAsync("radio")).IsEmpty();
		await Assert.That(await _db.GetMemberFeedsAsync(ann, null)).IsEmpty();
		await Assert.That(await _db.GetFeedMessageAsync(1) is NotFound).IsTrue();
		await Assert.That(await _db.GetFeedTapsAsync(null)).IsEmpty();
		await Assert.That(await _db.DeleteFeedKindAsync("radio")).IsFalse();
	}

	[Test]
	public async Task Purge_DropsLinesBeforeATime()
	{
		var ann = await NewPlayer("Ann");
		for (var id = 1; id <= 4; id++) await _db.AppendFeedMessageAsync(Line(id, ann, $"line {id}"), Keep());

		await Assert.That(await _db.PurgeFeedAsync("radio", "101.5", At.AddSeconds(3))).IsEqualTo(2);
		await Assert.That((await _db.GetFeedMessagesAsync("radio", "101.5", 0, 0)).Select(l => l.Id)).IsEquivalentTo(new long[] { 3, 4 });
		await Assert.That((await _db.GetFeedAsync("radio", "101.5")).Expect<SharpFeed>().Messages).IsEqualTo(2);
	}
}
