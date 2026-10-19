using System.Buffers.Binary;
using System.Text;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal enum WikiSemanticPlannedKind { Absent, Put, Delete }
internal readonly record struct WikiSemanticPlannedValue(WikiSemanticPlannedKind Kind, byte[]? Value);

internal sealed class WikiSemanticCapacityException(string dimension) : Exception(dimension)
{
	internal string Dimension => Message;
}

/// <summary>Only the final coalesced operations count towards the exact persisted delta.</summary>
internal sealed class WikiSemanticPlannedDelta(NativeReadReservation lifetime, CancellationToken cancellationToken)
{
	private sealed record Change(TableDef Table, byte[] Key, byte[]? Value, long Revision);
	private readonly List<Change> _changes = [];
	internal long Revision { get; private set; }
	internal NativeReadReservation Lifetime => lifetime;
	internal long RevisionFor(TableDef table, ReadOnlySpan<byte> key)
	{
		foreach (var change in _changes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (change.Table == table && change.Key.AsSpan().SequenceEqual(key)) return change.Revision;
		}
		return 0;
	}
	internal WikiSemanticPlannedValue Lookup(TableDef table, ReadOnlySpan<byte> key)
	{
		foreach (var change in _changes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (change.Table != table || !change.Key.AsSpan().SequenceEqual(key)) continue;
			if (change.Value is null) return new(WikiSemanticPlannedKind.Delete, null);
			lifetime.ReserveAdditional(32L + change.Value.Length);
			return new(WikiSemanticPlannedKind.Put, change.Value.ToArray());
		}
		return new(WikiSemanticPlannedKind.Absent, null);
	}
	internal long Bytes { get; private set; }
	internal void Put(TableDef table, byte[] key, byte[] value) => Add(table, key, value);
	internal void Delete(TableDef table, byte[] key) => Add(table, key, null);
	private void Add(TableDef table, byte[] key, byte[]? value)
	{
		if (key.Length + (long)(value?.Length ?? 0) > WikiSemanticLimits.MaxReadBytes) throw new WikiSemanticCapacityException("record-bytes");
		var index = -1;
		for (var i = 0; i < _changes.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_changes[i].Table == table && _changes[i].Key.AsSpan().SequenceEqual(key)) { index = i; break; }
		}
		var previous = index < 0 ? 0 : _changes[index].Key.Length + (long)(_changes[index].Value?.Length ?? 0);
		var total = checked(Bytes - previous + key.Length + (value?.Length ?? 0));
		if (total > WikiSemanticLimits.MaxDeltaBytes) throw new WikiSemanticCapacityException("delta-bytes");
		lifetime.ReserveAdditional(checked(192L + key.Length + (value?.Length ?? 0))); // own caller arrays and conservative list growth
		var revision = checked(Revision + 1);
		var change = new Change(table, key.ToArray(), value?.ToArray(), revision);
		if (index < 0) _changes.Add(change); else _changes[index] = change;
		Bytes = total;
		Revision = revision;
	}
	internal void Clear() { _changes.Clear(); Bytes = 0; Revision = checked(Revision + 1); }
	internal void Apply(ITx tx, CancellationToken ct, Action<TableDef>? putApplied = null, Action<TableDef>? deleteApplied = null)
	{
		foreach (var change in _changes)
		{
			ct.ThrowIfCancellationRequested();
			if (change.Value is null)
			{
				if (tx.Delete(change.Table, change.Key)) deleteApplied?.Invoke(change.Table);
			}
			else
			{
				tx.Put(change.Table, change.Key, change.Value);
				putApplied?.Invoke(change.Table);
			}
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
internal sealed class WikiSemanticWork : IDisposable
{
	internal readonly ITx Tx;
	internal readonly IBoundedReadTx Reader;
	internal readonly NativeReadBudget Budget;
	internal readonly NativeReadReservation Lifetime;
	internal readonly WikiSemanticPlannedDelta Delta;
	internal readonly WikiSemanticTables Tables;
	private readonly bool _ownsLifetime;
	private readonly bool _ownsDelta;
	private bool _emptyBase;
	private readonly Dictionary<(WikiSemanticRegistryKind Kind, string Identity), (ulong Id, byte[] Key, long Revision)> _registry = [];
	private readonly Dictionary<WikiSemanticRegistryKind, (ulong Next, byte[] Key, long Revision)> _next = [];
	private WikiSemanticWork(ITx tx, WikiSemanticTables tables, NativeReadBudget budget, NativeReadReservation lifetime, bool ownsLifetime, WikiSemanticPlannedDelta? delta)
	{
		Tx = tx; Tables = tables; Budget = budget;
		Reader = (IBoundedReadTx)tx;
		_ownsLifetime = ownsLifetime;
		Lifetime = lifetime;
		_ownsDelta = delta is null;
		Delta = delta ?? new(Lifetime, budget.CancellationToken);
	}
	internal static WikiSemanticWork Create(ITx tx, WikiSemanticTables tables, NativeReadBudget budget, NativeReadReservation? lifetime = null, WikiSemanticPlannedDelta? delta = null)
	{
		if (tx is not IBoundedReadTx) throw new NotSupportedException("Checked native reads are required.");
		if (delta is not null && !ReferenceEquals(lifetime, delta.Lifetime))
			throw new ArgumentException("The owning delta and work must share one allocation lifetime.", nameof(delta));
		var owns = lifetime is null;
		var scope = lifetime ?? budget.ReserveWorking(0);
		try
		{
			// Reserve before constructing the scope, dictionaries, delta or list headers.
			scope.ReserveAdditional(1024);
			return new WikiSemanticWork(tx, tables, budget, scope, owns, delta);
		}
		catch { if (owns) scope.Dispose(); throw; }
	}

	internal static WikiSemanticWork CreateEmptyBase(ITx tx, WikiSemanticTables tables, NativeReadBudget budget, NativeReadReservation lifetime, WikiSemanticPlannedDelta delta)
	{
		var work = Create(tx, tables, budget, lifetime, delta);
		work._emptyBase = true;
		return work;
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
		var planned = Delta.Lookup(table, key);
		if (planned.Kind == WikiSemanticPlannedKind.Delete) return null;
		if (planned.Kind == WikiSemanticPlannedKind.Put)
		{
			var value = planned.Value ?? throw new InvalidDataException("Missing planned value.");
			Lifetime.ReserveAdditional(checked(overhead + value.Length * (long)factor));
			return decode(value);
		}
		if (_emptyBase) return null;
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
	internal byte[] Lookup(WikiSemanticRegistryKind kind, string identity)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identity);
		if (identity.Length > WikiSemanticLimits.MaxReadBytes) throw new ArgumentException("Registry identity exceeds the record bound.", nameof(identity));
		return Bytes(33L + Encoding.UTF8.GetByteCount(identity), () => WikiSemanticKeys.RegistryLookup(kind, identity));
	}
	internal ulong Registry(WikiSemanticRegistryKind kind, string identity, bool create = true)
	{
		if (_registry.TryGetValue((kind, identity), out var known) && known.Revision == Delta.RevisionFor(Tables.Registry, known.Key)) return known.Id;
		var key = Lookup(kind, identity);
		var entry = Read(Tables.Registry, key, WikiSemanticStateCodec.Registry);
		if (entry is not null)
		{
			if (entry.Identity != identity || entry.Id == 0) throw new InvalidDataException("Registry hash collision or invalid ID.");
			Remember(kind, identity, entry.Id, key); return entry.Id;
		}
		_registry.Remove((kind, identity));
		if (!create) return 0;
		var next = _next.TryGetValue(kind, out var cached) && cached.Revision == Delta.RevisionFor(Tables.Metadata, cached.Key)
			? cached.Next : Counter(Key((byte)(20 + (byte)kind)), 1);
		if (next == 0 || next == ulong.MaxValue) throw new WikiSemanticCapacityException("registry-id");
		Lifetime.ReserveAdditional(128);
		var counterKey = Key((byte)(20 + (byte)kind));
		Delta.Put(Tables.Metadata, counterKey, Number(next + 1));
		var value = Bytes(32L + Encoding.UTF8.GetByteCount(identity), () => WikiSemanticStateCodec.Registry(new WikiSemanticRegistryEntry(next, identity)));
		Delta.Put(Tables.Registry, key, value);
		Delta.Put(Tables.Registry, Bytes(9, () => WikiSemanticKeys.RegistryReverse(kind, next)), value);
		_next[kind] = (checked(next + 1), counterKey, Delta.RevisionFor(Tables.Metadata, counterKey));
		Remember(kind, identity, next, key); return next;
	}
	private void Remember(WikiSemanticRegistryKind kind, string identity, ulong id, byte[] key)
	{
		if (!_registry.ContainsKey((kind, identity))) Lifetime.ReserveAdditional(192);
		_registry[(kind, identity)] = (id, key, Delta.RevisionFor(Tables.Registry, key));
	}
	public void Dispose() { if (_ownsDelta) Delta.Clear(); _registry.Clear(); _next.Clear(); if (_ownsLifetime) Lifetime.Dispose(); }
}
