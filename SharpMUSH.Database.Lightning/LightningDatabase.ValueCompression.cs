using System.Buffers;
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
/// Feed lines, attribute values and mail, compressed with zstd dictionaries.
/// <para>
/// A value is short, so compressing it alone saves little; a dictionary of what values of its kind share (the JSON's
/// field names, the markup's shape, common words, softcode's functions) lets each be compressed alone and still small,
/// so a value stays one read and one write by its key. A compressed value is <see cref="CompressedValue"/>, the
/// dictionary's id (two bytes, big-endian) and a zstd frame made with it. A value stored as it is begins with
/// <c>{</c>: one shorter than its kind's <see cref="CompressedTable.MinBytes"/>, one written before compression, or one
/// compression would not make smaller.
/// </para>
/// <para>
/// Each kind (<see cref="CompressedTable"/>) has dictionaries of its own. Dictionary <see cref="BundledDictionaryId"/>
/// ships with the server (made by <c>tools/BundledDictionaries</c>), so a new world compresses its first value. It is
/// never changed: values in every world are made with it, and a better default is a new file under a new reserved id.
/// A world's own dictionaries live in <see cref="Tables.Meta"/>, so a backup carries them with the values.
/// <see cref="CompactAsync"/> trains one once the world holds <see cref="TrainAtRows"/> values of the kind, and another
/// when it holds <see cref="RetrainGrowth"/> times the values the current one was trained on, keeping it only when it
/// compresses the world's values better by more than it takes itself; it then rewrites every value made without the
/// current dictionary, a batch per write.
/// </para>
/// <para>
/// Every dictionary the world keeps stays after its values are rewritten, and every run is logged, kept or not. A value
/// can always be read whatever happens to a rewrite, the log shows how a world's compression went, and
/// <see cref="UseDictionaryAsync"/> goes back to an earlier dictionary.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	/// <summary>The first byte of a value compressed with a dictionary.</summary>
	internal const byte CompressedValue = 0x01;

	/// <summary>The dictionary that ships with the server, in every kind.</summary>
	internal const int BundledDictionaryId = 0;

	/// <summary>Values of a kind the world holds before a dictionary of its own is trained.</summary>
	internal const int TrainAtRows = 100;

	/// <summary>How many times the values the current dictionary was trained on the world holds before another is trained.</summary>
	internal const int RetrainGrowth = 4;

	/// <summary>One sampled value in this many is held out of training, to judge the dictionary on.</summary>
	private const int HeldOutEvery = 5;

	/// <summary>How many times its dictionary's size a training set reads: zstd asks for about a hundred.</summary>
	private const int TrainingMultiple = 100;

	/// <summary>How much smaller a new dictionary must make the held-out values than the current one, in percent, to replace it.</summary>
	internal const int RetrainGainPercent = 3;

	/// <summary>Values rewritten per write transaction by the pass after a dictionary is trained.</summary>
	internal const int RecompressBatch = 1000;

	/// <summary>
	/// Feed lines. 16 KiB is where, on chat lines, a larger dictionary stops paying for itself; the bands are short lines
	/// (emits, short says), chat, and long poses.
	/// </summary>
	internal static readonly CompressedTable FeedLines = new(CompressedValues.FeedLines, "feed", Tables.FeedMessage,
		"feed-lines-0.zdict", DictionaryBytes: 16 * 1024, TrainingBands: [200, 1000], MinBytes: 0);

	/// <summary>
	/// Attribute values, as <c>MarkupTextSerializer</c> JSON. Half of a game's are a few bytes (a flag, a number, a
	/// dbref) and compress to nothing, so those under 64 bytes stay as they are; measured on the packages' softcode, a
	/// 32 KiB dictionary does a little better than 16 KiB, and more does little more.
	/// </summary>
	internal static readonly CompressedTable AttributeValues = new(CompressedValues.Attributes, "attr", Tables.AttrVal,
		"attribute-values-0.zdict", DictionaryBytes: 32 * 1024, TrainingBands: [256, 2048], MinBytes: 64);

	/// <summary>Mail, each a whole <see cref="MailRecord"/>: short notes, letters, and long ones.</summary>
	internal static readonly CompressedTable MailValues = new(CompressedValues.Mail, "mail", Tables.Mail,
		"mail-0.zdict", DictionaryBytes: 16 * 1024, TrainingBands: [400, 1500], MinBytes: 64);

	internal static CompressedTable TableOf(CompressedValues values) => values switch
	{
		CompressedValues.FeedLines => FeedLines,
		CompressedValues.Attributes => AttributeValues,
		CompressedValues.Mail => MailValues,
		_ => throw new ArgumentOutOfRangeException(nameof(values), values, null)
	};

	/// <summary>
	/// Dictionaries read so far, by the store's epoch, kind and id: one is never changed once written, but a world
	/// swapped in under the same store has dictionaries of its own under the same ids.
	/// </summary>
	private readonly ConcurrentDictionary<(long Epoch, string Kind, int Id), DictionaryCodec> _dictionaries = new();

	/// <summary>
	/// <paramref name="plain"/> as stored, compressed with the dictionary the transaction sees as current (as it is when
	/// it is short or that is smaller). Read in the same write as the value, so a value is never written with a
	/// dictionary a later commit replaced.
	/// </summary>
	private byte[] EncodeValue(ITx tx, CompressedTable table, byte[] plain)
	{
		if (plain.Length < table.MinBytes) return plain;
		var id = CurrentDictionaryId(tx, table);
		return Dictionary(tx, table, id) is { } dictionary ? dictionary.Compress(plain, id) : plain;
	}

	/// <summary>
	/// A stored value as it was before compression; null when it cannot be read (its dictionary gone, or a frame this
	/// server cannot read). A value stored as it is comes back unchanged.
	/// </summary>
	private byte[]? DecodeValue(ITx tx, CompressedTable table, byte[] stored)
	{
		if (!IsCompressed(stored)) return stored;
		return Dictionary(tx, table, BinaryPrimitives.ReadUInt16BigEndian(stored.AsSpan(1))) is { } dictionary
			? dictionary.Decompress(stored.AsSpan(3))
			: null;
	}

	private static bool IsCompressed(ReadOnlySpan<byte> stored) => stored.Length > 3 && stored[0] == CompressedValue;

	/// <summary>Whether a stored value was compressed with dictionary <paramref name="id"/>.</summary>
	private static bool MadeWith(ReadOnlySpan<byte> stored, int id)
		=> IsCompressed(stored) && BinaryPrimitives.ReadUInt16BigEndian(stored[1..]) == id;

	/// <summary>A stored value's length before compression, from the frame's header when it is compressed.</summary>
	private static long PlainLength(byte[] stored)
		=> IsCompressed(stored) && ZstandardDecoder.TryGetMaxDecompressedLength(stored.AsSpan(3), out var length)
			? length
			: stored.Length;

	private static int CurrentDictionaryId(ITx tx, CompressedTable table) => ReadDictionaryState(tx, table)?.Id ?? BundledDictionaryId;

	private static DictionaryStateRecord? ReadDictionaryState(ITx tx, CompressedTable table)
		=> tx.TryGet(Tables.Meta, table.StateKey, out var bytes) ? Codec.Deserialize<DictionaryStateRecord>(bytes) : null;

	/// <summary>The dictionary <paramref name="id"/> of a kind, read the first time it is asked for; null if the world has none.</summary>
	private DictionaryCodec? Dictionary(ITx tx, CompressedTable table, int id)
	{
		var cacheKey = (Store.Epoch, table.MetaName, id);
		if (_dictionaries.TryGetValue(cacheKey, out var cached)) return cached;
		var bytes = id == BundledDictionaryId ? table.Bundled : tx.TryGet(Tables.Meta, table.DictionaryKey(id), out var stored) ? stored : null;
		return bytes is null ? null : _dictionaries.GetOrAdd(cacheKey, _ => new DictionaryCodec(bytes));
	}

	public async ValueTask<DictionaryCompaction> CompactAsync(CompressedValues values, DateTimeOffset at,
		CancellationToken cancellationToken = default)
	{
		var table = TableOf(values);
		var (state, rows) = Store.Read(tx => (ReadDictionaryState(tx, table) ?? new DictionaryStateRecord { Id = BundledDictionaryId },
			tx.Count(table.Table)));

		var trained = false;
		if (rows >= TrainAtRows && rows >= state.Rows * RetrainGrowth)
		{
			var current = state;
			var (samples, eligible) = Store.Read(tx => TrainingSet(tx, table));
			// Judged on values it was not trained on: a dictionary holds much of what it saw, so the values it saw
			// compress better than any the world writes later.
			var held = samples.Where((_, index) => index % HeldOutEvery == 0).ToList();
			var dictionary = Train(table, samples.Where((_, index) => index % HeldOutEvery != 0).ToList());
			var (before, after) = Store.Read(tx => Measure(held, dictionary, Dictionary(tx, table, current.Id)!));
			var better = dictionary is not null && Better(before, after, held.Count, dictionary.Length, eligible);
			(state, trained) = await Store.WriteAsync(tx =>
			{
				// Not kept, the values it was trained on still count, so the next try waits for the world to grow again.
				var written = ReadDictionaryState(tx, table) ?? current;
				var id = better ? NextDictionaryId(tx, table) : null;
				var next = id is { } kept
					? new DictionaryStateRecord { Id = kept, Rows = rows }
					: written with { Rows = rows };
				if (id is { } newId) tx.Put(Tables.Meta, table.DictionaryKey(newId), dictionary!);
				tx.Put(Tables.Meta, table.StateKey, Codec.Serialize(next));
				tx.Put(Tables.Meta, table.RunKey(NextDictionaryRun(tx, table)), Codec.Serialize(new DictionaryRunRecord
				{
					AtMs = at.ToUnixTimeMilliseconds(), Rows = rows, SampleRows = samples.Count, HeldOutRows = held.Count,
					CurrentBytes = before, CandidateBytes = after, From = written.Id, Kept = id
				}));
				return (next, id is not null);
			}, cancellationToken);
		}

		if (state.Complete) return new DictionaryCompaction(trained, 0);
		return new DictionaryCompaction(trained, await RewriteWithAsync(table, state.Id, cancellationToken));
	}

	public ValueTask<StoredDictionaries> GetDictionariesAsync(CompressedValues values, CancellationToken cancellationToken = default)
	{
		var table = TableOf(values);
		return ValueTask.FromResult(Store.Read(tx =>
		{
			var current = CurrentDictionaryId(tx, table);
			var runs = tx.Range(Tables.Meta, table.RunPrefix)
				.Select(entry => Codec.Deserialize<DictionaryRunRecord>(entry.Value))
				.Select(run => new DictionaryRun(DateTimeOffset.FromUnixTimeMilliseconds(run.AtMs), run.Rows, run.SampleRows,
					run.HeldOutRows, run.CurrentBytes, run.CandidateBytes, run.From, run.Kept))
				.ToList();
			var used = new Dictionary<int, long>();
			long uncompressed = 0;
			foreach (var (_, value) in tx.Range(table.Table, []))
			{
				if (IsCompressed(value))
				{
					var id = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(1));
					used[id] = used.GetValueOrDefault(id) + 1;
				}
				else uncompressed++;
			}

			var trainedAt = runs.Where(run => run.Kept is not null).ToDictionary(run => run.Kept!.Value, run => run.At);
			DictionaryGeneration Generation(int id, int bytes)
				=> new(id, trainedAt.TryGetValue(id, out var when) ? when : null, bytes, used.GetValueOrDefault(id), id == current);
			IReadOnlyList<DictionaryGeneration> generations =
			[
				Generation(BundledDictionaryId, table.Bundled.Length),
				.. tx.Range(Tables.Meta, table.DictionaryPrefix).Select(entry => Generation(table.DictionaryIdOf(entry.Key), entry.Value.Length))
			];
			return new StoredDictionaries(generations, runs, uncompressed);
		}));
	}

	public async ValueTask<Found<long>> UseDictionaryAsync(CompressedValues values, int id, CancellationToken cancellationToken = default)
	{
		var table = TableOf(values);
		var known = await Store.WriteAsync(tx =>
		{
			// A value records its dictionary in 16 bits, and the key keeps only those.
			if (id is < 0 or > ushort.MaxValue) return false;
			if (id != BundledDictionaryId && !tx.TryGet(Tables.Meta, table.DictionaryKey(id), out _)) return false;
			// The values its successor was trained on still count, so the world is not retrained straight away.
			var state = ReadDictionaryState(tx, table) ?? new DictionaryStateRecord { Id = BundledDictionaryId };
			tx.Put(Tables.Meta, table.StateKey, Codec.Serialize(state with { Id = id, Complete = false }));
			return true;
		}, cancellationToken);
		if (!known) return new NotFound();

		return await RewriteWithAsync(table, id, cancellationToken);
	}

	/// <summary>
	/// Rewrites every value made without dictionary <paramref name="id"/>, then marks the pass done, unless another
	/// dictionary became current meanwhile, whose own pass then runs.
	/// </summary>
	private async ValueTask<long> RewriteWithAsync(CompressedTable table, int id, CancellationToken cancellationToken)
	{
		var rewritten = await RecompressAsync(table, id, cancellationToken);
		await Store.WriteAsync(tx =>
		{
			var state = ReadDictionaryState(tx, table) ?? new DictionaryStateRecord { Id = BundledDictionaryId };
			if (state.Id == id)
				tx.Put(Tables.Meta, table.StateKey, Codec.Serialize(state with { Complete = true }));
			return true;
		}, cancellationToken);
		var epoch = Store.Epoch;
		foreach (var key in _dictionaries.Keys.Where(key => key.Epoch != epoch)) _dictionaries.TryRemove(key, out _);
		return rewritten;
	}

	/// <summary>Rewrites every value not made with dictionary <paramref name="id"/>, a batch per write.</summary>
	private async ValueTask<long> RecompressAsync(CompressedTable table, int id, CancellationToken cancellationToken)
	{
		long rewritten = 0;
		byte[]? after = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var (count, last) = await Store.WriteAsync(tx =>
			{
				// Another dictionary became current since the pass began; its own pass rewrites the values.
				if (CurrentDictionaryId(tx, table) != id) return (0, (byte[]?)null);
				var rows = (after is null ? tx.Range(table.Table, []) : tx.RangeFrom(table.Table, [], after, null))
					.Take(RecompressBatch).ToList();
				var resized = new List<(byte[] Key, long Delta)>();
				var batch = 0;
				foreach (var (key, value) in rows.Where(row => !MadeWith(row.Value, id)))
				{
					// A value that can't be read stays as it is; one that is not smaller compressed is already as it should be.
					if (DecodeValue(tx, table, value) is not { } plain) continue;
					var stored = EncodeValue(tx, table, plain);
					if (stored.AsSpan().SequenceEqual(value)) continue;
					tx.Put(table.Table, key, stored);
					resized.Add((key, stored.Length - value.Length));
					batch++;
				}

				if (ReferenceEquals(table, FeedLines)) AdjustFeedStoredBytes(tx, resized);
				return (batch, rows.Count < RecompressBatch ? null : rows[^1].Key);
			}, cancellationToken);

			rewritten += count;
			if (last is null) return rewritten;
			after = last;
		}
	}

	/// <summary>The next free dictionary id of a kind, past every one the world holds; null once the ids run out.</summary>
	private static int? NextDictionaryId(ITx tx, CompressedTable table)
	{
		var last = tx.RangeReverse(Tables.Meta, table.DictionaryPrefix).Select(entry => table.DictionaryIdOf(entry.Key)).FirstOrDefault();
		return last < ushort.MaxValue ? last + 1 : null;
	}

	private static uint NextDictionaryRun(ITx tx, CompressedTable table)
		=> tx.RangeReverse(Tables.Meta, table.RunPrefix)
			.Select(entry => BinaryPrimitives.ReadUInt32BigEndian(entry.Key.AsSpan(table.RunPrefix.Length)) + 1)
			.FirstOrDefault(1u);

	/// <summary>
	/// <paramref name="samples"/> (values the candidate was not trained on) compressed with <paramref name="current"/>
	/// and with <paramref name="candidate"/>, in bytes; -1 for the candidate when there is none.
	/// </summary>
	private static (long Before, long After) Measure(List<byte[]> samples, byte[]? candidate, DictionaryCodec current)
	{
		static long Size(List<byte[]> samples, DictionaryCodec dictionary)
			=> samples.Sum(sample => (long)dictionary.Compress(sample, 1).Length);
		if (candidate is null) return (Size(samples, current), -1);
		return (Size(samples, current), Size(samples, new DictionaryCodec(candidate)));
	}

	/// <summary>
	/// Whether a candidate is worth keeping: it makes the held-out values at least <see cref="RetrainGainPercent"/>
	/// percent smaller, and what that saves across the world's <paramref name="rows"/> values it would compress is
	/// more than the dictionary's own <paramref name="bytes"/>.
	/// </summary>
	private static bool Better(long before, long after, int samples, int bytes, long rows)
		=> samples > 0 && after * 100 <= before * (100 - RetrainGainPercent) && (before - after) * rows / samples > bytes;

	/// <summary>
	/// Values as they were before compression, up to <see cref="TrainingMultiple"/> times the kind's dictionary size,
	/// shared between its <see cref="CompressedTable.TrainingBands"/>: each band gets an equal part of the budget (what a
	/// small band leaves passes on to the others) and its values are taken evenly across the table. A world of short
	/// chat lines still trains on its few long poses, and the other way round. Values too short to be compressed are
	/// left out, and so not counted in <c>Eligible</c>, the values a dictionary would compress.
	/// </summary>
	internal (List<byte[]> Samples, long Eligible) TrainingSet(ITx tx, CompressedTable table)
	{
		var sizes = tx.Range(table.Table, []).Select(entry => PlainLength(entry.Value)).ToList();
		var bands = table.TrainingBands.Length + 1;
		var total = new long[bands];
		long eligible = 0;
		foreach (var size in sizes.Where(size => size >= table.MinBytes))
		{
			total[table.Band(size)] += size;
			eligible++;
		}

		// Smallest band first, so the budget a band can't use goes to the bands that can.
		var budget = new long[bands];
		var left = (long)TrainingMultiple * table.DictionaryBytes;
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
		foreach (var ((_, value), size) in tx.Range(table.Table, []).Zip(sizes))
		{
			if (size < table.MinBytes) continue;
			var band = table.Band(size);
			if (seen[band]++ % stride[band] != 0 || used[band] + size > budget[band]) continue;
			if (DecodeValue(tx, table, value) is not { } plain) continue;
			used[band] += size;
			samples.Add(plain);
		}

		return (samples, eligible);
	}

	/// <summary>A dictionary trained on <paramref name="samples"/>, or null when zstd cannot make one of them.</summary>
	private byte[]? Train(CompressedTable table, List<byte[]> samples)
	{
		try
		{
			using var dictionary = ZstandardDictionary.Train(samples.SelectMany(sample => sample).ToArray(),
				samples.Select(sample => sample.Length).ToArray(), table.DictionaryBytes);
			return dictionary.Data.ToArray();
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
		{
			_logger.LogWarning(ex, "No {Kind} dictionary could be trained from {Samples} values", table.MetaName, samples.Count);
			return null;
		}
	}
}

/// <summary>One kind of compressed value: the table it lives in, its bundled dictionary, and how its dictionaries are made.</summary>
/// <param name="Values">Which kind it is.</param>
/// <param name="MetaName">The prefix of its keys in <see cref="Tables.Meta"/>: <c>&lt;name&gt;.dict</c> (the current dictionary),
/// <c>&lt;name&gt;.dict:</c> and an id (the world's dictionaries), <c>&lt;name&gt;.dictrun:</c> and a number (the training log).</param>
/// <param name="Table">Where its values are stored, a value per key.</param>
/// <param name="Resource">The bundled dictionary's file under <c>Resources/</c>.</param>
/// <param name="DictionaryBytes">The size of a dictionary trained for it.</param>
/// <param name="TrainingBands">Where the length bands that share the training budget start, in bytes before compression.</param>
/// <param name="MinBytes">Values shorter than this are stored as they are.</param>
internal sealed record CompressedTable(CompressedValues Values, string MetaName, TableDef Table, string Resource,
	int DictionaryBytes, int[] TrainingBands, int MinBytes)
{
	public byte[] StateKey { get; } = Keys.Str(MetaName + ".dict");

	public byte[] DictionaryPrefix { get; } = Keys.Str(MetaName + ".dict:");

	public byte[] RunPrefix { get; } = Keys.Str(MetaName + ".dictrun:");

	public byte[] DictionaryKey(int id) => Keys.Concat(DictionaryPrefix, [(byte)(id >> 8), (byte)id]);

	public int DictionaryIdOf(byte[] key) => BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(DictionaryPrefix.Length));

	public byte[] RunKey(uint run)
	{
		var number = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(number, run);
		return Keys.Concat(RunPrefix, number);
	}

	public int Band(long size) => TrainingBands.Count(start => size >= start);

	/// <summary>The bundled dictionary's bytes, as shipped in this assembly.</summary>
	public byte[] Bundled => _bundled.Value;

	private readonly Lazy<byte[]> _bundled = new(() =>
	{
		using var stream = typeof(LightningDatabase).Assembly.GetManifestResourceStream($"SharpMUSH.Database.Lightning.{Resource}")
			?? throw new InvalidOperationException($"The bundled dictionary {Resource} is missing from the assembly.");
		using var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return bytes.ToArray();
	});
}

/// <summary>
/// A dictionary with the encoders and decoders made with it, kept for the next value: making one loads the dictionary,
/// which costs several times what compressing a short value does.
/// </summary>
internal sealed class DictionaryCodec(byte[] dictionary)
{
	private readonly ZstandardDictionary _dictionary = ZstandardDictionary.Create(dictionary, ZstandardCompressionOptions.DefaultQuality);
	private readonly ConcurrentBag<ZstandardEncoder> _encoders = [];
	private readonly ConcurrentBag<ZstandardDecoder> _decoders = [];

	/// <summary><paramref name="plain"/> as <see cref="LightningDatabase.CompressedValue"/>, <paramref name="id"/> and a frame; itself when that is not smaller.</summary>
	public byte[] Compress(byte[] plain, int id)
	{
		var encoder = _encoders.TryTake(out var pooled) ? pooled : new ZstandardEncoder(_dictionary, ZstandardCompressionOptions.DefaultWindowLog2);
		var buffer = ArrayPool<byte>.Shared.Rent(3 + (int)ZstandardEncoder.GetMaxCompressedLength(plain.Length));
		try
		{
			encoder.Reset();
			encoder.SetSourceLength(plain.Length);
			var status = encoder.Compress(plain, buffer.AsSpan(3), out var consumed, out var written, isFinalBlock: true);
			if (status != OperationStatus.Done || consumed != plain.Length)
			{
				encoder.Dispose();
				return plain;
			}

			_encoders.Add(encoder);
			if (written + 3 >= plain.Length) return plain;
			buffer[0] = LightningDatabase.CompressedValue;
			BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(1), (ushort)id);
			return buffer.AsSpan(0, written + 3).ToArray();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	/// <summary>A frame made with this dictionary, decompressed; null when it can't be.</summary>
	public byte[]? Decompress(ReadOnlySpan<byte> frame)
	{
		if (!ZstandardDecoder.TryGetMaxDecompressedLength(frame, out var length) || length is <= 0 or > int.MaxValue) return null;
		var plain = new byte[length];
		var decoder = _decoders.TryTake(out var pooled) ? pooled : new ZstandardDecoder(_dictionary);
		decoder.Reset();
		var status = decoder.Decompress(frame, plain, out _, out var written);
		if (status != OperationStatus.Done)
		{
			decoder.Dispose();
			return null;
		}

		_decoders.Add(decoder);
		return written == plain.Length ? plain : plain[..written];
	}
}
