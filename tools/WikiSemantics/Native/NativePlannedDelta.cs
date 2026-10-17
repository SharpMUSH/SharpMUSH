using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tools.WikiSemantics.Native;

internal sealed class NativeCapacityException(string dimension) : Exception(dimension)
{
	internal string Dimension => Message;
}

/// <summary>Only the final coalesced operations count towards the exact persisted delta.</summary>
internal sealed class NativePlannedDelta(NativeReadReservation lifetime, CancellationToken cancellationToken)
{
	private sealed record Change(TableDef Table, byte[] Key, byte[]? Value);
	private readonly List<Change> _changes = [];
	internal long Bytes { get; private set; }
	internal void Put(TableDef table, byte[] key, byte[] value) => Add(table, key, value);
	internal void Delete(TableDef table, byte[] key) => Add(table, key, null);
	private void Add(TableDef table, byte[] key, byte[]? value)
	{
		if (key.Length + (long)(value?.Length ?? 0) > NativeFixtureLimits.MaxReadBytes) throw new NativeCapacityException("record-bytes");
		var index = -1;
		for (var i = 0; i < _changes.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_changes[i].Table == table && _changes[i].Key.AsSpan().SequenceEqual(key)) { index = i; break; }
		}
		var previous = index < 0 ? 0 : _changes[index].Key.Length + (long)(_changes[index].Value?.Length ?? 0);
		var total = checked(Bytes - previous + key.Length + (value?.Length ?? 0));
		if (total > NativeFixtureLimits.MaxDeltaBytes) throw new NativeCapacityException("delta-bytes");
		lifetime.ReserveAdditional(128); // record plus conservative list growth/old-array overlap
		var change = new Change(table, key, value);
		if (index < 0) _changes.Add(change); else _changes[index] = change;
		Bytes = total;
	}
	internal void Clear() => _changes.Clear();
	internal void Apply(ITx tx, CancellationToken ct)
	{
		foreach (var change in _changes)
		{
			ct.ThrowIfCancellationRequested();
			if (change.Value is null) tx.Delete(change.Table, change.Key); else tx.Put(change.Table, change.Key, change.Value);
		}
		ct.ThrowIfCancellationRequested();
	}
}

/// <summary>
/// One synchronous accounting scope. Encoders reserve 4x payload plus 512 bytes for headers,
/// writers, streams, temporary UTF8/hash buffers and delegates. Decoders reserve 8x row bytes
/// plus 1024 for the four-byte-length page/fact format. Compact state envelopes reserve
/// 24x row bytes plus 2048 for UTF16 strings, per-string headers, arrays/builders and copies.
/// All reservations remain live until the complete plan/read has dropped its references.
/// </summary>
internal sealed class NativeWork : IDisposable
{
	internal readonly ITx Tx;
	internal readonly IBoundedReadTx Reader;
	internal readonly NativeReadBudget Budget;
	internal readonly NativeReadReservation Lifetime;
	internal readonly NativePlannedDelta Delta;
	internal readonly NativeTables Tables;
	private readonly bool _ownsLifetime;
	private readonly Dictionary<(NativeRegistryKind Kind, string Identity), ulong> _registry = [];
	private readonly Dictionary<NativeRegistryKind, ulong> _next = [];
	private NativeWork(ITx tx, NativeTables tables, NativeReadBudget budget, NativeReadReservation lifetime, bool ownsLifetime)
	{
		Tx = tx; Tables = tables; Budget = budget;
		Reader = (IBoundedReadTx)tx;
		_ownsLifetime = ownsLifetime;
		Lifetime = lifetime;
		Delta = new(Lifetime, budget.CancellationToken);
	}
	internal static NativeWork Create(ITx tx, NativeTables tables, NativeReadBudget budget, NativeReadReservation? lifetime = null)
	{
		if (tx is not IBoundedReadTx) throw new NotSupportedException("Checked native reads are required.");
		var owns = lifetime is null;
		var scope = lifetime ?? budget.ReserveWorking(0);
		try
		{
			// Reserve before constructing the scope, dictionaries, delta or list headers.
			scope.ReserveAdditional(1024);
			return new NativeWork(tx, tables, budget, scope, owns);
		}
		catch { if (owns) scope.Dispose(); throw; }
	}

	internal byte[] Bytes(long estimate, Func<byte[]> encode)
	{
		Lifetime.ReserveAdditional(checked(512 + estimate * 4));
		var bytes = encode();
		if (bytes.Length > estimate) throw new InvalidDataException("Encoder exceeded its reserved estimate.");
		return bytes;
	}
	internal byte[] Key(byte tag, ulong id = 0) => Bytes(id == 0 ? 1 : 9, () =>
	{
		if (id == 0) return [tag];
		var key = new byte[9]; key[0] = tag; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), id); return key;
	});
	internal byte[] Pair(ulong collection, ulong classId) => Bytes(16, () =>
	{
		var key = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(key, collection); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(8), classId); return key;
	});
	internal byte[] Number(ulong value) => Bytes(8, () => { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; });
	internal T? Read<T>(TableDef table, byte[] key, Func<byte[], T> decode, int factor = 24, int overhead = 2048) where T : class
	{
		if (Reader.ReadValue(table, key, Budget) is not NativeReadValue owner) return null;
		using (owner)
		{
			Lifetime.ReserveAdditional(checked(overhead + owner.Value.Length * (long)factor));
			return decode(owner.Value);
		}
	}
	internal byte[]? ReadBytes(TableDef table, byte[] key) => Read(table, key, b => b.ToArray());
	internal ulong Counter(byte[] key, ulong absent = 0)
	{
		var bytes = ReadBytes(Tables.Metadata, key);
		if (bytes is null) return absent;
		if (bytes.Length != 8) throw new InvalidDataException("Malformed counter.");
		return BinaryPrimitives.ReadUInt64BigEndian(bytes);
	}
	internal byte[] Lookup(NativeRegistryKind kind, string identity)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identity);
		if (identity.Length > NativeFixtureLimits.MaxReadBytes) throw new ArgumentException("Registry identity exceeds the record bound.", nameof(identity));
		return Bytes(33L + Encoding.UTF8.GetByteCount(identity), () => NativeKeys.RegistryLookup(kind, identity));
	}
	internal ulong Registry(NativeRegistryKind kind, string identity, bool create = true)
	{
		if (_registry.TryGetValue((kind, identity), out var known)) return known;
		var key = Lookup(kind, identity);
		var entry = Read(Tables.Registry, key, NativeStateCodec.Registry);
		if (entry is not null)
		{
			if (entry.Identity != identity || entry.Id == 0) throw new InvalidDataException("Registry hash collision or invalid ID.");
			Remember(kind, identity, entry.Id); return entry.Id;
		}
		if (!create) return 0;
		if (!_next.TryGetValue(kind, out var next)) next = Counter(Key((byte)(20 + (byte)kind)), 1);
		if (next == 0 || next == ulong.MaxValue) throw new NativeCapacityException("registry-id");
		Lifetime.ReserveAdditional(128);
		_next[kind] = checked(next + 1);
		Delta.Put(Tables.Metadata, Key((byte)(20 + (byte)kind)), Number(next + 1));
		var value = Bytes(32L + Encoding.UTF8.GetByteCount(identity), () => NativeStateCodec.Registry(new NativeRegistryEntry(next, identity)));
		Delta.Put(Tables.Registry, key, value);
		Delta.Put(Tables.Registry, Bytes(9, () => NativeKeys.RegistryReverse(kind, next)), value);
		Remember(kind, identity, next); return next;
	}
	private void Remember(NativeRegistryKind kind, string identity, ulong id)
	{
		Lifetime.ReserveAdditional(192); _registry.Add((kind, identity), id);
	}
	public void Dispose() { Delta.Clear(); _registry.Clear(); _next.Clear(); if (_ownsLifetime) Lifetime.Dispose(); }
}
