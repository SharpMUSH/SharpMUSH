using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Feed lines compressed with a zstd dictionary: the bundled one from a world's first line, the world's own once it
/// holds enough lines, a new one as it grows, the stored sizes that follow, and a line that cannot be read.
/// </summary>
public class FeedCompressionTests
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

	private static readonly DBRef Speaker = new(42, 1793700000000);

	private static readonly string[] Ships = ["Zephyrine", "Quorvax", "Mollusk-Seven", "Brindlewick"];

	private static readonly string[] Planets = ["Thessaly Prime", "Orrin's Reach", "Vantablack Station", "Kelp Moon"];

	/// <summary>A line in a vocabulary of its own, so a dictionary trained on such lines beats one that never saw it.</summary>
	private static SharpFeedMessage ShipLine(long id, string kind = "radio", string key = "101.5")
	{
		var text = $"Captain, the {Ships[id % Ships.Length]} reports a plasma-conduit rupture near {Planets[id / 3 % Planets.Length]}, sector {id % 17}.";
		return new SharpFeedMessage(id, kind, key, At.AddSeconds(id), Speaker, "Navigator", Speaker, "Navigator", null, "",
			FeedStyles.Say, MarkupText.Plain(text), Line: MarkupText.Plain($"<Bridge> Navigator says, \"{text}\""));
	}

	/// <summary>Another vocabulary, for a world whose talk moves on.</summary>
	private static SharpFeedMessage TavernLine(long id)
	{
		var text = $"The barkeep slides a tankard of {(id % 2 == 0 ? "honeyed mead" : "black stout")} to table {id % 9}, where the minstrel tunes a lute for verse {id}.";
		return new SharpFeedMessage(id, "radio", "88.1", At.AddSeconds(id), Speaker, "Bard", Speaker, "Bard", null, "",
			FeedStyles.Pose, MarkupText.Plain(text));
	}

	/// <summary>A scene pose, several sentences long.</summary>
	private static SharpFeedMessage PoseLine(long id)
	{
		var text = string.Join(" ", Enumerable.Range(0, 12).Select(n => ShipLine(id + n).Text.ToPlainText()));
		return new SharpFeedMessage(id, "scene", "Bridge", At.AddSeconds(id), Speaker, "Navigator", Speaker, "Navigator", null, "",
			FeedStyles.Pose, MarkupText.Plain(text));
	}

	/// <summary>A line of text no dictionary can predict.</summary>
	private static SharpFeedMessage NoiseLine(long id)
	{
		var text = Convert.ToHexString(new Random((int)id).GetItems<byte>(Enumerable.Range(0, 256).Select(b => (byte)b).ToArray(), 120));
		return new SharpFeedMessage(id, "radio", "13.7", At.AddSeconds(id), Speaker, "Static", Speaker, "Static", null, "",
			FeedStyles.Say, MarkupText.Plain(text));
	}

	/// <summary>Enough lines that a dictionary of their own saves more than it takes.</summary>
	private const int Lines = 500;

	private static FeedSettings Keep() => new(0, 0, 0, null, true, "say");

	private static int DictionaryOf(byte[] value)
		=> value[0] == LightningDatabase.CompressedLine ? (value[1] << 8) | value[2] : -1;

	private List<byte[]> Values() => _db.Store.Read(tx => tx.Range(Tables.FeedMessage, []).Select(entry => entry.Value).ToList());

	private long OnDisk() => _db.Store.Read(tx => tx.Range(Tables.FeedMessage, [])
		.Sum(entry => (long)entry.Value.Length + entry.Key.Length * 2 + 8));

	private async Task<long> StoredTotal() => (await _db.GetFeedUsageAsync()).Sum(usage => usage.StoredBytes);

	private FeedDictionaryRecord? State() => _db.Store.Read(tx =>
		tx.TryGet(Tables.Meta, Keys.Str("feed.dict"), out var bytes) ? Codec.Deserialize<FeedDictionaryRecord>(bytes) : null);

	private int WorldDictionaries() => _db.Store.Read(tx => tx.Range(Tables.Meta, Keys.Str("feed.dict:")).Count());

	/// <summary>
	/// Lines in every world are compressed with the bundled dictionary; a change to it would leave them unreadable, so
	/// its bytes are pinned. A better default is a new file under a new id.
	/// </summary>
	[Test]
	public async Task TheBundledDictionary_NeverChanges()
	{
		var hash = Convert.ToHexString(SHA256.HashData(LightningDatabase.BundledFeedDictionary));
		await Assert.That(hash).IsEqualTo(BundledHash);
	}

	private const string BundledHash = "174BB6A430BCE2BD135CB6107507E7E51DDE72BE0028800DFFAF1394A8086F52";

	[Test]
	public async Task ANewWorld_CompressesItsFirstLine_WithTheBundledDictionary()
	{
		var line = ShipLine(1);
		await _db.AppendFeedMessageAsync(line, Keep());

		var value = Values().Single();
		await Assert.That(DictionaryOf(value)).IsEqualTo(LightningDatabase.BundledDictionaryId);
		await Assert.That(value.Length).IsLessThan(LightningDatabase.FeedLineJson(line).Length);

		var read = (await _db.GetFeedMessageAsync(1)).Expect<SharpFeedMessage>();
		await Assert.That(read.Text.ToPlainText()).IsEqualTo(line.Text.ToPlainText());
		await Assert.That(read.Line!.ToPlainText()).IsEqualTo(line.Line!.ToPlainText());
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
	}

	/// <summary>Lines stored as JSON before compression (a migrated world) are rewritten with the bundled dictionary.</summary>
	[Test]
	public async Task LinesStoredAsJson_AreCompressed_BelowTheTrainingThreshold()
	{
		for (var id = 1; id <= 10; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		// As a migrated world holds them: JSON, with the feed's stored size what that takes.
		await _db.Store.WriteAsync(tx =>
		{
			foreach (var (key, value) in tx.Range(Tables.FeedMessage, []).ToList())
				tx.Put(Tables.FeedMessage, key, Codec.Serialize(_db.DecodeLine(tx, key, value)));
			var feedKey = Keys.Concat(Keys.Str("radio"), Keys.Sep, Keys.Str("101.5"));
			tx.TryGet(Tables.Feed, feedKey, out var bytes);
			var onDisk = tx.Range(Tables.FeedMessage, []).Sum(entry => (long)entry.Value.Length + entry.Key.Length * 2 + 8);
			tx.Put(Tables.Feed, feedKey, Codec.Serialize(Codec.Deserialize<FeedRecord>(bytes) with { StoredBytes = onDisk }));
		});
		var json = Values();
		await Assert.That(json.All(value => value[0] == (byte)'{')).IsTrue();
		var jsonTotal = json.Sum(value => (long)value.Length);

		var compaction = await _db.CompactFeedLinesAsync(At);

		await Assert.That(compaction).IsEqualTo(new FeedCompaction(false, 10));
		await Assert.That(Values().All(value => DictionaryOf(value) == LightningDatabase.BundledDictionaryId)).IsTrue();
		await Assert.That(Values().Sum(value => (long)value.Length)).IsLessThan(jsonTotal);
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
		await Assert.That(State()).IsEqualTo(new FeedDictionaryRecord { Id = 0, Lines = 0, Complete = true });
		await Assert.That((await _db.CompactFeedLinesAsync(At)).Rewritten).IsEqualTo(0).Because("the pass is done");
	}

	/// <summary>
	/// At <see cref="LightningDatabase.TrainAtLines"/> lines the world trains a dictionary of its own; it compresses the
	/// world's lines better than the bundled one, so every line is rewritten with it and reads back as it was.
	/// </summary>
	[Test]
	public async Task AWorldWithEnoughLines_TrainsItsOwnDictionary()
	{
		var lines = Enumerable.Range(1, Lines).Select(id => ShipLine(id, key: id % 2 == 0 ? "101.5" : "99.1")).ToList();
		foreach (var line in lines) await _db.AppendFeedMessageAsync(line, Keep());
		var before = await StoredTotal();

		var compaction = await _db.CompactFeedLinesAsync(At);

		await Assert.That(compaction).IsEqualTo(new FeedCompaction(true, Lines));
		await Assert.That(State()).IsEqualTo(new FeedDictionaryRecord { Id = 1, Lines = Lines, Complete = true });
		await Assert.That(Values().All(value => DictionaryOf(value) == 1)).IsTrue();
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
		await Assert.That(await StoredTotal()).IsLessThan(before);
		foreach (var line in lines)
		{
			var read = (await _db.GetFeedMessageAsync(line.Id)).Expect<SharpFeedMessage>();
			await Assert.That(read.Text.ToPlainText()).IsEqualTo(line.Text.ToPlainText());
		}

		// A line sent now is made with the world's dictionary.
		await _db.AppendFeedMessageAsync(ShipLine(1000), Keep());
		await Assert.That(DictionaryOf(_db.Store.Read(tx =>
		{
			tx.TryGet(Tables.FeedMessageId, Keys.Dbref(1000), out var key);
			tx.TryGet(Tables.FeedMessage, key, out var value);
			return value;
		}))).IsEqualTo(1);
	}

	/// <summary>
	/// A world whose lines a dictionary of its own would save less on than it takes keeps the bundled one, and the next
	/// try waits until the world has grown <see cref="LightningDatabase.RetrainGrowth"/> times.
	/// </summary>
	[Test]
	public async Task AWorldADictionaryCannotHelp_KeepsTheBundledOne()
	{
		for (var id = 1; id <= LightningDatabase.TrainAtLines; id++) await _db.AppendFeedMessageAsync(NoiseLine(id), Keep());

		var compaction = await _db.CompactFeedLinesAsync(At);

		await Assert.That(compaction).IsEqualTo(new FeedCompaction(false, 0));
		await Assert.That(State()).IsEqualTo(new FeedDictionaryRecord { Id = 0, Lines = LightningDatabase.TrainAtLines, Complete = true });
		await Assert.That(WorldDictionaries()).IsEqualTo(0);
		await Assert.That(Values().All(value => DictionaryOf(value) == LightningDatabase.BundledDictionaryId)).IsTrue();
	}

	/// <summary>
	/// When the world has grown <see cref="LightningDatabase.RetrainGrowth"/> times past what its dictionary saw and
	/// its talk has moved on, a new dictionary replaces it and the lines are rewritten; the old one is kept.
	/// </summary>
	[Test]
	public async Task AGrownWorld_ReplacesItsDictionary_AndKeepsTheOldOne()
	{
		for (var id = 1; id <= Lines; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);
		for (var id = 10001; id <= 10000 + Lines * (LightningDatabase.RetrainGrowth - 1); id++)
			await _db.AppendFeedMessageAsync(TavernLine(id), Keep());

		var compaction = await _db.CompactFeedLinesAsync(At);

		await Assert.That(compaction.Trained).IsTrue();
		await Assert.That(State()!.Id).IsEqualTo(2);
		await Assert.That(WorldDictionaries()).IsEqualTo(2);
		await Assert.That(Values().All(value => DictionaryOf(value) == 2)).IsTrue();
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
		var ship = (await _db.GetFeedMessageAsync(5)).Expect<SharpFeedMessage>();
		await Assert.That(ship.Text.ToPlainText()).IsEqualTo(ShipLine(5).Text.ToPlainText());
	}

	/// <summary>
	/// Every training run is logged, kept or not, and every dictionary the world kept is listed with when it was trained
	/// and how many lines use it.
	/// </summary>
	[Test]
	public async Task EveryRunIsLogged_AndEveryDictionaryListed()
	{
		for (var id = 1; id <= Lines; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);
		for (var id = 10001; id <= 10000 + Lines * (LightningDatabase.RetrainGrowth - 1); id++)
			await _db.AppendFeedMessageAsync(NoiseLine(id), Keep());
		var later = At.AddDays(30);
		await _db.CompactFeedLinesAsync(later);

		var dictionaries = await _db.GetFeedDictionariesAsync();

		await Assert.That(dictionaries.Runs.Count).IsEqualTo(2);
		var (first, second) = (dictionaries.Runs[0], dictionaries.Runs[1]);
		await Assert.That(first.At).IsEqualTo(At);
		await Assert.That(first.Lines).IsEqualTo(Lines);
		await Assert.That(first.From).IsEqualTo(LightningDatabase.BundledDictionaryId);
		await Assert.That(first.Kept).IsEqualTo(1);
		await Assert.That(first.CandidateBytes).IsLessThan(first.CurrentBytes);
		await Assert.That(first.HeldOutLines).IsEqualTo((first.SampleLines + 4) / 5);
		await Assert.That(second.At).IsEqualTo(later);
		await Assert.That(second.From).IsEqualTo(1);
		await Assert.That(second.Lines).IsEqualTo(Lines * LightningDatabase.RetrainGrowth);

		var generations = dictionaries.Generations;
		await Assert.That(generations.Select(generation => generation.Id)).IsEquivalentTo(second.Kept is { } kept ? [0, 1, kept] : new[] { 0, 1 });
		await Assert.That(generations[0].TrainedAt).IsNull();
		await Assert.That(generations[0].Bytes).IsEqualTo(LightningDatabase.BundledFeedDictionary.Length);
		await Assert.That(generations[1].TrainedAt).IsEqualTo(At);
		await Assert.That(generations.Single(generation => generation.Current).Id).IsEqualTo(State()!.Id);
		await Assert.That(generations.Sum(generation => generation.Lines) + dictionaries.Uncompressed).IsEqualTo(Values().Count);
	}

	/// <summary>
	/// Going back to an earlier dictionary rewrites the lines with it and leaves the later one in place, and the next
	/// dictionary trained takes a new id rather than the later one's.
	/// </summary>
	[Test]
	public async Task GoingBack_RewritesTheLines_AndKeepsTheLaterDictionary()
	{
		for (var id = 1; id <= Lines; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);
		var later = _db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Concat(Keys.Str("feed.dict:"), [0, 1]), out var bytes) ? bytes : null);

		var rolled = await _db.UseFeedDictionaryAsync(LightningDatabase.BundledDictionaryId);

		await Assert.That(rolled.Expect<long>()).IsEqualTo(Lines);
		await Assert.That(Values().All(value => DictionaryOf(value) == LightningDatabase.BundledDictionaryId)).IsTrue();
		await Assert.That(State()).IsEqualTo(new FeedDictionaryRecord { Id = 0, Lines = Lines, Complete = true });
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
		await _db.AppendFeedMessageAsync(ShipLine(Lines + 1), Keep());
		await Assert.That(DictionaryOf(Values()[^1])).IsEqualTo(LightningDatabase.BundledDictionaryId);

		for (var id = 10001; id <= 10000 + Lines * (LightningDatabase.RetrainGrowth - 1); id++)
			await _db.AppendFeedMessageAsync(TavernLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);

		await Assert.That(State()!.Id).IsEqualTo(2);
		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Concat(Keys.Str("feed.dict:"), [0, 1]), out var bytes) ? bytes : null))
			.IsEquivalentTo(later!);
		var ship = (await _db.GetFeedMessageAsync(5)).Expect<SharpFeedMessage>();
		await Assert.That(ship.Text.ToPlainText()).IsEqualTo(ShipLine(5).Text.ToPlainText());
	}

	/// <summary>A dictionary the world never had can't be gone back to, and nothing changes.</summary>
	[Test]
	public async Task GoingBackToAnUnknownDictionary_ChangesNothing()
	{
		await _db.AppendFeedMessageAsync(ShipLine(1), Keep());
		var before = Values().Single();

		await Assert.That(await _db.UseFeedDictionaryAsync(9) is NotFound).IsTrue();
		await Assert.That(Values().Single()).IsEquivalentTo(before);
		await Assert.That(State()).IsNull();
	}

	/// <summary>
	/// A world of short lines that also holds a few long poses trains on all of them: the long ones have a band of the
	/// budget to themselves rather than the one line in hundreds an even pick would give them.
	/// </summary>
	[Test]
	public async Task Training_TakesLongLinesAsWellAsShortOnes()
	{
		var poses = Enumerable.Range(1, 20).Select(id => 100000L + id * 400).ToHashSet();
		for (var id = 1L; id <= 8000; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		foreach (var id in poses) await _db.AppendFeedMessageAsync(PoseLine(id), Keep());

		var samples = _db.Store.Read(_db.TrainingSet);

		await Assert.That(samples.Sum(sample => (long)sample.Length)).IsLessThanOrEqualTo(LightningDatabase.TrainingBytes);
		await Assert.That(samples.Count(sample => sample.Length >= 1000)).IsEqualTo(poses.Count);
		await Assert.That(samples.Count(sample => sample.Length < 1000)).IsGreaterThan(1000);
	}

	/// <summary>Every way a compressed line goes takes its stored size with it.</summary>
	[Test]
	public async Task StoredSizes_FollowCompressedLines()
	{
		for (var id = 1; id <= Lines; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);

		await _db.DeleteFeedMessageAsync(3);
		await _db.AppendFeedMessageAsync(ShipLine(Lines + 1), new FeedSettings(50, 0, 0, null, true, "say"));
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk());
		await _db.RenameFeedAsync("radio", "101.5", "101.5-ship-to-shore");
		await Assert.That(await StoredTotal()).IsEqualTo(OnDisk()).Because("a renamed line's key is longer");
		await _db.PurgeFeedAsync("radio", "101.5-ship-to-shore", null);
		await Assert.That(await StoredTotal()).IsEqualTo(0);
	}

	/// <summary>A line that can't be read is left as stored by a rewrite, so it can still be read if its dictionary comes back.</summary>
	[Test]
	public async Task ALineThatCannotBeRead_IsLeftAsItIsByARewrite()
	{
		await _db.AppendFeedMessageAsync(ShipLine(7), Keep());
		var tampered = await _db.Store.WriteAsync(tx =>
		{
			var (key, value) = tx.Range(Tables.FeedMessage, []).Single();
			value[1] = 0x7F;
			tx.Put(Tables.FeedMessage, key, value);
			tx.Put(Tables.Meta, Keys.Str("feed.dict"), Codec.Serialize(new FeedDictionaryRecord { Id = 0 }));
			return value;
		});

		var compaction = await _db.CompactFeedLinesAsync(At);

		await Assert.That(compaction.Rewritten).IsEqualTo(0);
		await Assert.That(Values().Single()).IsEquivalentTo(tampered);
	}

	/// <summary>
	/// A world swapped in under the same store has dictionaries of its own under the same ids, so lines written after the
	/// swap use its dictionary, not the one the outgoing world had cached.
	/// </summary>
	[Test]
	public async Task AWorldSwappedIn_UsesItsOwnDictionary()
	{
		for (var id = 1; id <= Lines; id++) await _db.AppendFeedMessageAsync(ShipLine(id), Keep());
		await _db.CompactFeedLinesAsync(At);
		var incoming = _path + ".incoming";
		await using (var replacement = Create(incoming)) await replacement.Migrate();
		_db.Store.SwapDirectory(incoming, _path + ".previous");
		await _db.Migrate();

		await _db.AppendFeedMessageAsync(ShipLine(1000), Keep());

		await Assert.That(DictionaryOf(Values().Single())).IsEqualTo(LightningDatabase.BundledDictionaryId);
		var read = (await _db.GetFeedMessageAsync(1000)).Expect<SharpFeedMessage>();
		await Assert.That(read.Text.ToPlainText()).IsEqualTo(ShipLine(1000).Text.ToPlainText());
		Directory.Delete(_path + ".previous", recursive: true);
	}

	/// <summary>A line whose dictionary is gone reads as a line saying so, under its id, rather than failing the read.</summary>
	[Test]
	public async Task ALineThatCannotBeRead_ReadsAsSaying()
	{
		await _db.AppendFeedMessageAsync(ShipLine(7), Keep());
		await _db.Store.WriteAsync(tx =>
		{
			var (key, value) = tx.Range(Tables.FeedMessage, []).Single();
			value[1] = 0x7F;
			tx.Put(Tables.FeedMessage, key, value);
		});

		var read = (await _db.GetFeedMessageAsync(7)).Expect<SharpFeedMessage>();
		await Assert.That(read.Id).IsEqualTo(7);
		await Assert.That(read.Text.ToPlainText()).IsEqualTo("This line can't be read.");
	}
}
