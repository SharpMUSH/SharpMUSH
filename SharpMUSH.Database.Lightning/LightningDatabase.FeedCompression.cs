using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Feed lines compressed with a zstd dictionary.
/// <para>
/// A line is short, so compressing it alone saves little; a dictionary of what lines share (the record's field names,
/// the markup's JSON, the channel prefixes and common words) lets each line be compressed alone and still small, so a
/// line stays one value read and written by its key. A stored line is <see cref="CompressedLine"/>, the dictionary's id
/// (two bytes, big-endian) and a zstd frame made with it; a line written before compression is its JSON, which begins
/// with <c>{</c>, and reads as it is.
/// </para>
/// <para>
/// Dictionary <see cref="BundledDictionaryId"/> ships with the server (<c>Resources/feed-lines-0.zdict</c>, made by
/// <c>tools/FeedDictionary</c>), so a new world compresses its first line. It is never changed: lines in every world
/// are made with it, and a better default is a new file under a new reserved id. A world's own dictionaries live in
/// <see cref="Tables.Meta"/>, so a backup carries them with the lines. <see cref="CompactFeedLinesAsync"/> trains one
/// once the world holds <see cref="TrainAtLines"/> lines, and another when it holds <see cref="RetrainGrowth"/> times
/// the lines the current one was trained on, keeping it only when it compresses the world's lines better by more than
/// it takes itself (with a small world's lines the bundled dictionary stays); it then
/// rewrites every line made without the current dictionary, a batch per write.
/// </para>
/// <para>
/// Every dictionary the world keeps stays in <see cref="Tables.Meta"/> after its lines are rewritten (one is trained
/// each time the world grows fourfold, so a world of a million lines holds about seven), and every run is logged under
/// <c>feed.dictrun:</c>, kept or not. A line can always be read whatever happens to a rewrite, the log shows how a
/// world's compression went, and <see cref="UseFeedDictionaryAsync"/> goes back to an earlier dictionary.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	/// <summary>The first byte of a line compressed with a dictionary.</summary>
	internal const byte CompressedLine = 0x01;

	/// <summary>The dictionary that ships with the server.</summary>
	internal const int BundledDictionaryId = 0;

	/// <summary>Lines the world holds before a dictionary of its own is trained.</summary>
	internal const int TrainAtLines = 100;

	/// <summary>How many times the lines the current dictionary was trained on the world holds before another is trained.</summary>
	internal const int RetrainGrowth = 4;

	/// <summary>One sampled line in this many is held out of training, to judge the dictionary on.</summary>
	private const int HeldOutEvery = 5;

	/// <summary>The most JSON read to train a dictionary: zstd asks for about a hundred times the dictionary's size.</summary>
	internal const long TrainingBytes = 100L * DictionaryBytes;

	/// <summary>
	/// Where the length bands that share the training budget start, in bytes of JSON: short lines (emits, short says),
	/// chat, and long poses.
	/// </summary>
	private static readonly int[] TrainingBands = [200, 1000];

	/// <summary>A dictionary's size; measured on chat lines, 16 KiB is where a larger one stops paying for itself.</summary>
	internal const int DictionaryBytes = 16 * 1024;

	/// <summary>How much smaller a new dictionary must make the training lines than the current one, in percent, to replace it.</summary>
	internal const int RetrainGainPercent = 3;

	/// <summary>Lines rewritten per write transaction by the pass after a dictionary is trained.</summary>
	internal const int RecompressBatch = 1000;

	private static readonly byte[] FeedDictionaryStateKey = Keys.Str("feed.dict");

	private static readonly byte[] FeedDictionaryPrefix = Keys.Str("feed.dict:");

	private static byte[] FeedDictionaryKey(int id) => Keys.Concat(FeedDictionaryPrefix, [(byte)(id >> 8), (byte)id]);

	private static readonly byte[] FeedDictionaryRunPrefix = Keys.Str("feed.dictrun:");

	private static byte[] FeedDictionaryRunKey(uint run)
	{
		var number = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(number, run);
		return Keys.Concat(FeedDictionaryRunPrefix, number);
	}

	private static int FeedDictionaryIdOf(byte[] key) => BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(FeedDictionaryPrefix.Length));

	/// <summary>The bundled dictionary's bytes, as shipped in this assembly.</summary>
	internal static byte[] BundledFeedDictionary => BundledDictionaryBytes.Value;

	private static readonly Lazy<byte[]> BundledDictionaryBytes = new(() =>
	{
		using var stream = typeof(LightningDatabase).Assembly.GetManifestResourceStream("SharpMUSH.Database.Lightning.feed-lines-0.zdict")
			?? throw new InvalidOperationException("The bundled feed line dictionary is missing from the assembly.");
		using var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return bytes.ToArray();
	});

	/// <summary>
	/// Dictionaries read so far, by the store's epoch and id: one is never changed once written, only deleted when no
	/// line uses it, but a world swapped in under the same store has dictionaries of its own under the same ids.
	/// </summary>
	private readonly ConcurrentDictionary<(long Epoch, int Id), ZstandardDictionary> _feedDictionaries = new();

	/// <summary>
	/// A line as stored, compressed with the dictionary the transaction sees as current (as JSON in the rare case that is
	/// smaller). Read in the same write as the line, so a line is never written with a dictionary a later commit replaced.
	/// </summary>
	private byte[] EncodeLine(ITx tx, FeedMessageRecord record)
	{
		var id = CurrentFeedDictionaryId(tx);
		return Compress(Codec.Serialize(record), id, FeedDictionary(tx, id)!);
	}

	private static int CurrentFeedDictionaryId(ITx tx) => ReadFeedDictionaryState(tx)?.Id ?? BundledDictionaryId;

	private static byte[] Compress(byte[] json, int id, ZstandardDictionary dictionary)
	{
		var buffer = new byte[3 + ZstandardEncoder.GetMaxCompressedLength(json.Length)];
		if (!ZstandardEncoder.TryCompress(json, buffer.AsSpan(3), out var written, dictionary, ZstandardCompressionOptions.DefaultWindowLog2)
			|| written + 3 >= json.Length)
		{
			return json;
		}

		buffer[0] = CompressedLine;
		BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(1), (ushort)id);
		return buffer[..(written + 3)];
	}

	/// <summary>
	/// A stored line, compressed or not. A line that cannot be read (its dictionary gone, or a form this server does not
	/// know) reads as a line saying so, under its id from <paramref name="key"/>, rather than failing the read.
	/// </summary>
	internal FeedMessageRecord DecodeLine(ITx tx, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		=> ReadLine(tx, value) ?? UnreadableLine(key);

	/// <summary>A stored line, or null when it cannot be read.</summary>
	private FeedMessageRecord? ReadLine(ITx tx, ReadOnlySpan<byte> value)
	{
		if (value.Length > 0 && value[0] == (byte)'{') return Codec.Deserialize<FeedMessageRecord>(value);
		return value.Length > 3 && value[0] == CompressedLine
			&& FeedDictionary(tx, BinaryPrimitives.ReadUInt16BigEndian(value[1..])) is { } dictionary
			&& Decompress(value[3..], dictionary) is { } json
				? Codec.Deserialize<FeedMessageRecord>(json)
				: null;
	}

	private static byte[]? Decompress(ReadOnlySpan<byte> frame, ZstandardDictionary dictionary)
	{
		if (!ZstandardDecoder.TryGetMaxDecompressedLength(frame, out var length) || length is <= 0 or > int.MaxValue) return null;
		var json = new byte[length];
		return ZstandardDecoder.TryDecompress(frame, json, out var written, dictionary) ? json[..written] : null;
	}

	private static FeedMessageRecord UnreadableLine(ReadOnlySpan<byte> key) => new()
	{
		Id = Keys.ReadDbref(key[^8..]),
		Text = MarkupTextSerializer.Serialize(MarkupText.Plain("This line can't be read."))
	};

	/// <summary>The dictionary <paramref name="id"/>, read the first time it is asked for; null if it is gone.</summary>
	private ZstandardDictionary? FeedDictionary(ITx tx, int id)
	{
		var cacheKey = (Store.Epoch, id);
		if (_feedDictionaries.TryGetValue(cacheKey, out var cached)) return cached;
		byte[]? bytes = id == BundledDictionaryId ? BundledFeedDictionary : tx.TryGet(Tables.Meta, FeedDictionaryKey(id), out var stored) ? stored : null;
		return bytes is null ? null : _feedDictionaries.GetOrAdd(cacheKey, _ => ZstandardDictionary.Create(bytes, ZstandardCompressionOptions.DefaultQuality));
	}

	private static FeedDictionaryRecord? ReadFeedDictionaryState(ITx tx)
		=> tx.TryGet(Tables.Meta, FeedDictionaryStateKey, out var bytes) ? Codec.Deserialize<FeedDictionaryRecord>(bytes) : null;

	/// <summary>Whether a stored line was compressed with dictionary <paramref name="id"/>.</summary>
	private static bool MadeWith(ReadOnlySpan<byte> value, int id)
		=> value.Length > 3 && value[0] == CompressedLine && BinaryPrimitives.ReadUInt16BigEndian(value[1..]) == id;

	public async ValueTask<FeedCompaction> CompactFeedLinesAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
	{
		var (state, lines) = Store.Read(tx => (ReadFeedDictionaryState(tx) ?? new FeedDictionaryRecord { Id = BundledDictionaryId },
			tx.Range(Tables.Feed, []).Sum(entry => (long)Codec.Deserialize<FeedRecord>(entry.Value).Messages)));

		var trained = false;
		if (lines >= TrainAtLines && lines >= state.Lines * RetrainGrowth)
		{
			var current = state;
			var samples = Store.Read(TrainingSet);
			// Judged on lines it was not trained on: a dictionary holds much of what it saw, so the lines it saw compress
			// better than any the world writes later.
			var held = samples.Where((_, index) => index % HeldOutEvery == 0).ToList();
			var dictionary = Train(samples.Where((_, index) => index % HeldOutEvery != 0).ToList());
			var (before, after) = Store.Read(tx => Measure(held, dictionary, FeedDictionary(tx, current.Id)!));
			var better = dictionary is not null && Better(before, after, held.Count, dictionary.Length, lines);
			(state, trained) = await Store.WriteAsync(tx =>
			{
				// Not kept, the lines it was trained on still count, so the next try waits for the world to grow again.
				var written = ReadFeedDictionaryState(tx) ?? current;
				var id = better ? NextFeedDictionaryId(tx) : null;
				var next = id is { } kept
					? new FeedDictionaryRecord { Id = kept, Lines = lines }
					: written with { Lines = lines };
				if (id is { } newId) tx.Put(Tables.Meta, FeedDictionaryKey(newId), dictionary!);
				tx.Put(Tables.Meta, FeedDictionaryStateKey, Codec.Serialize(next));
				tx.Put(Tables.Meta, FeedDictionaryRunKey(NextFeedDictionaryRun(tx)), Codec.Serialize(new FeedDictionaryRunRecord
				{
					AtMs = at.ToUnixTimeMilliseconds(), Lines = lines, SampleLines = samples.Count, HeldOutLines = held.Count,
					CurrentBytes = before, CandidateBytes = after, From = written.Id, Kept = id
				}));
				return (next, id is not null);
			}, cancellationToken);
		}

		if (state.Complete) return new FeedCompaction(trained, 0);
		return new FeedCompaction(trained, await RewriteWithAsync(state.Id, cancellationToken));
	}

	public ValueTask<FeedDictionaries> GetFeedDictionariesAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx =>
		{
			var current = CurrentFeedDictionaryId(tx);
			var runs = tx.Range(Tables.Meta, FeedDictionaryRunPrefix)
				.Select(entry => Codec.Deserialize<FeedDictionaryRunRecord>(entry.Value))
				.Select(run => new FeedDictionaryRun(DateTimeOffset.FromUnixTimeMilliseconds(run.AtMs), run.Lines, run.SampleLines,
					run.HeldOutLines, run.CurrentBytes, run.CandidateBytes, run.From, run.Kept))
				.ToList();
			var used = new Dictionary<int, long>();
			long uncompressed = 0;
			foreach (var (_, value) in tx.Range(Tables.FeedMessage, []))
			{
				if (value.Length > 3 && value[0] == CompressedLine)
				{
					var id = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(1));
					used[id] = used.GetValueOrDefault(id) + 1;
				}
				else uncompressed++;
			}

			var trainedAt = runs.Where(run => run.Kept is not null).ToDictionary(run => run.Kept!.Value, run => run.At);
			FeedDictionaryGeneration Generation(int id, int bytes)
				=> new(id, trainedAt.TryGetValue(id, out var when) ? when : null, bytes, used.GetValueOrDefault(id), id == current);
			IReadOnlyList<FeedDictionaryGeneration> generations =
			[
				Generation(BundledDictionaryId, BundledFeedDictionary.Length),
				.. tx.Range(Tables.Meta, FeedDictionaryPrefix).Select(entry => Generation(FeedDictionaryIdOf(entry.Key), entry.Value.Length))
			];
			return new FeedDictionaries(generations, runs, uncompressed);
		}));

	public async ValueTask<Found<long>> UseFeedDictionaryAsync(int id, CancellationToken cancellationToken = default)
	{
		var known = await Store.WriteAsync(tx =>
		{
			// A line records its dictionary in 16 bits, and the key keeps only those.
			if (id is < 0 or > ushort.MaxValue) return false;
			if (id != BundledDictionaryId && !tx.TryGet(Tables.Meta, FeedDictionaryKey(id), out _)) return false;
			// The lines its successor was trained on still count, so the world is not retrained straight away.
			var state = ReadFeedDictionaryState(tx) ?? new FeedDictionaryRecord { Id = BundledDictionaryId };
			tx.Put(Tables.Meta, FeedDictionaryStateKey, Codec.Serialize(state with { Id = id, Complete = false }));
			return true;
		}, cancellationToken);
		if (!known) return new NotFound();

		return await RewriteWithAsync(id, cancellationToken);
	}

	/// <summary>
	/// Rewrites every line made without dictionary <paramref name="id"/>, then marks the pass done, unless another
	/// dictionary became current meanwhile, whose own pass then runs.
	/// </summary>
	private async ValueTask<long> RewriteWithAsync(int id, CancellationToken cancellationToken)
	{
		var rewritten = await RecompressAsync(id, cancellationToken);
		await Store.WriteAsync(tx =>
		{
			var state = ReadFeedDictionaryState(tx) ?? new FeedDictionaryRecord { Id = BundledDictionaryId };
			if (state.Id == id)
				tx.Put(Tables.Meta, FeedDictionaryStateKey, Codec.Serialize(state with { Complete = true }));
			return true;
		}, cancellationToken);
		var epoch = Store.Epoch;
		foreach (var key in _feedDictionaries.Keys.Where(key => key.Epoch != epoch)) _feedDictionaries.TryRemove(key, out _);
		return rewritten;
	}

	/// <summary>The next free dictionary id, past every one the world holds; null once the ids run out.</summary>
	private static int? NextFeedDictionaryId(ITx tx)
	{
		var last = tx.RangeReverse(Tables.Meta, FeedDictionaryPrefix).Select(entry => FeedDictionaryIdOf(entry.Key)).FirstOrDefault();
		return last < ushort.MaxValue ? last + 1 : null;
	}

	private static uint NextFeedDictionaryRun(ITx tx)
		=> tx.RangeReverse(Tables.Meta, FeedDictionaryRunPrefix)
			.Select(entry => BinaryPrimitives.ReadUInt32BigEndian(entry.Key.AsSpan(FeedDictionaryRunPrefix.Length)) + 1)
			.FirstOrDefault(1u);

	/// <summary>A line's JSON as the store writes it before compressing, for <c>tools/FeedDictionary</c> to train on.</summary>
	internal static byte[] FeedLineJson(SharpFeedMessage message) => Codec.Serialize(ToRecord(message));

	/// <summary>
	/// <paramref name="samples"/> (lines the candidate was not trained on) compressed with <paramref name="current"/>
	/// and with <paramref name="candidate"/>, in bytes; -1 for the candidate when there is none.
	/// </summary>
	private static (long Before, long After) Measure(List<byte[]> samples, byte[]? candidate, ZstandardDictionary current)
	{
		long Size(ZstandardDictionary dictionary) => samples.Sum(sample => (long)Compress(sample, 1, dictionary).Length);
		if (candidate is null) return (Size(current), -1);
		using var trained = ZstandardDictionary.Create(candidate, ZstandardCompressionOptions.DefaultQuality);
		return (Size(current), Size(trained));
	}

	/// <summary>
	/// Whether a candidate is worth keeping: it makes the held-out lines at least <see cref="RetrainGainPercent"/>
	/// percent smaller, and what that saves across the world's <paramref name="lines"/> is more than the dictionary's
	/// own <paramref name="bytes"/>.
	/// </summary>
	private static bool Better(long before, long after, int samples, int bytes, long lines)
		=> samples > 0 && after * 100 <= before * (100 - RetrainGainPercent) && (before - after) * lines / samples > bytes;

	/// <summary>
	/// Lines as JSON, up to <see cref="TrainingBytes"/>, shared between the <see cref="TrainingBands"/>: each band gets an
	/// equal part of the budget (what a small band leaves passes on to the others) and its lines are taken evenly across
	/// the table. A world of short chat lines still trains on its few long poses, and the other way round.
	/// </summary>
	internal List<byte[]> TrainingSet(ITx tx)
	{
		var sizes = tx.Range(Tables.FeedMessage, []).Select(entry => JsonLength(entry.Value)).ToList();
		var bands = TrainingBands.Length + 1;
		var total = new long[bands];
		foreach (var size in sizes) total[Band(size)] += size;

		// Smallest band first, so the budget a band can't use goes to the bands that can.
		var budget = new long[bands];
		var left = TrainingBytes;
		var present = Enumerable.Range(0, bands).Where(band => total[band] > 0).OrderBy(band => total[band]).ToList();
		foreach (var (band, index) in present.Select((band, index) => (band, index)))
		{
			budget[band] = Math.Min(total[band], left / (present.Count - index));
			left -= budget[band];
		}

		var stride = Enumerable.Range(0, bands)
			.Select(band => budget[band] == 0 ? long.MaxValue : Math.Max(1, (total[band] + budget[band] - 1) / budget[band]))
			.ToArray();
		var seen = new long[bands];
		var used = new long[bands];
		var samples = new List<byte[]>();
		foreach (var ((_, value), size) in tx.Range(Tables.FeedMessage, []).Zip(sizes))
		{
			var band = Band(size);
			if (seen[band]++ % stride[band] != 0 || used[band] + size > budget[band]) continue;
			if (ReadLine(tx, value) is not { } line) continue;
			used[band] += size;
			samples.Add(Codec.Serialize(line));
		}

		return samples;
	}

	private static int Band(long size) => TrainingBands.Count(start => size >= start);

	/// <summary>A stored line's length as JSON, from the frame's header when it is compressed.</summary>
	private static long JsonLength(byte[] value)
		=> value.Length > 3 && value[0] == CompressedLine && ZstandardDecoder.TryGetMaxDecompressedLength(value.AsSpan(3), out var length)
			? length
			: value.Length;

	/// <summary>A dictionary trained on <paramref name="samples"/>, or null when zstd cannot make one of them.</summary>
	private byte[]? Train(List<byte[]> samples)
	{
		try
		{
			using var dictionary = ZstandardDictionary.Train(samples.SelectMany(sample => sample).ToArray(),
				samples.Select(sample => sample.Length).ToArray(), DictionaryBytes);
			return dictionary.Data.ToArray();
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
		{
			_logger.LogWarning(ex, "No feed line dictionary could be trained from {Samples} lines", samples.Count);
			return null;
		}
	}

	/// <summary>Rewrites every line not made with dictionary <paramref name="id"/>, a batch per write, keeping each feed's stored size.</summary>
	private async ValueTask<long> RecompressAsync(int id, CancellationToken cancellationToken)
	{
		long rewritten = 0;
		byte[]? after = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var (count, last) = await Store.WriteAsync(tx =>
			{
				// Another dictionary became current since the pass began; its own pass rewrites the lines.
				if (CurrentFeedDictionaryId(tx) != id) return (0, (byte[]?)null);
				var rows = (after is null ? tx.Range(Tables.FeedMessage, []) : tx.RangeFrom(Tables.FeedMessage, [], after, null))
					.Take(RecompressBatch).ToList();
				var changed = new Dictionary<(string Kind, string Key), long>();
				var batch = 0;
				foreach (var (key, value) in rows.Where(row => !MadeWith(row.Value, id)))
				{
					// A line that can't be read stays as it is: rewriting it would store the notice in its place.
					if (ReadLine(tx, value) is not { } line) continue;
					var stored = EncodeLine(tx, line);
					tx.Put(Tables.FeedMessage, key, stored);
					var feed = SplitFeed(key.AsSpan(0, key.Length - 9));
					changed[feed] = changed.GetValueOrDefault(feed) + stored.Length - value.Length;
					batch++;
				}

				foreach (var ((kind, key), delta) in changed)
				{
					var feed = ReadFeed(tx, kind, key);
					tx.Put(Tables.Feed, FeedKey(kind, key), Codec.Serialize(feed with { StoredBytes = feed.StoredBytes + delta }));
				}

				return (batch, rows.Count < RecompressBatch ? null : rows[^1].Key);
			}, cancellationToken);

			rewritten += count;
			if (last is null) return rewritten;
			after = last;
		}
	}
}
