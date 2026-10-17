using System.Text;

namespace SharpMUSH.Tools.WikiSemantics.Native;

internal static class NativeVisibilityCatalog
{
	internal const ulong MaximumClasses = 1024;
	internal const ulong MaximumBytes = 1 << 20;
	internal static string Identity(ulong collection, byte[] descriptor) => collection.ToString("X16") + Convert.ToBase64String(descriptor);
	internal static ulong Move(NativeWork work, NativeSummaryState? previous, PageSnapshot page, ulong collection, bool hasFacts)
	{
		var descriptor = work.Bytes(NativeFixtureLimits.MaxDescriptorBytes, () => NativeStateCodec.Descriptor(page.Visibility));
		if (descriptor.Length > NativeFixtureLimits.MaxDescriptorBytes) throw new NativeCapacityException("descriptor-bytes");
		var counts = new Dictionary<ulong, (ulong Count, ulong Bytes)>();
		work.Lifetime.ReserveAdditional(512);
		(ulong Count, ulong Bytes) Totals(ulong id)
		{
			if (counts.TryGetValue(id, out var found)) return found;
			var totals = (work.Counter(work.Key(30, id)), work.Counter(work.Key(31, id)));
			work.Lifetime.ReserveAdditional(128); counts.Add(id, totals); return totals;
		}
		string ClassIdentity(ulong id, byte[] bytes)
		{
			work.Lifetime.ReserveAdditional(128 + bytes.Length * 4L); return Identity(id, bytes);
		}
		ulong MetadataBytes(ulong id, string identity, NativeCatalogEntry entry)
		{
			var row = work.Bytes(entry.Descriptor.Length + 32L, () => NativeStateCodec.Catalog(entry));
			var lookup = work.Bytes(Encoding.UTF8.GetByteCount(identity) + 32L, () => NativeStateCodec.Registry(new NativeRegistryEntry(id, identity)));
			return checked((ulong)(16 + row.Length + 33 + lookup.Length));
		}
		if (previous is { ClassId: > 0 })
		{
			var oldKey = work.Pair(previous.CollectionId, previous.ClassId);
			var old = work.Read(work.Tables.Catalog, oldKey, NativeStateCodec.Catalog) ?? throw new InvalidDataException("Missing old class.");
			if (old.References < 1) throw new InvalidDataException("Invalid class reference count.");
			if (hasFacts && previous.CollectionId == collection && old.Descriptor.AsSpan().SequenceEqual(descriptor)) return previous.ClassId;
			if (old.References == 1)
			{
				var identity = ClassIdentity(previous.CollectionId, old.Descriptor);
				var totals = Totals(previous.CollectionId);
				var bytes = MetadataBytes(previous.ClassId, identity, old);
				if (totals.Count == 0 || totals.Bytes < bytes) throw new InvalidDataException("Catalog counter underflow.");
				counts[previous.CollectionId] = (totals.Count - 1, totals.Bytes - bytes);
				work.Delta.Delete(work.Tables.Catalog, oldKey);
				// Retain the reverse identity history and old partition version; never reuse this ID.
				work.Delta.Delete(work.Tables.Registry, work.Lookup(NativeRegistryKind.Class, identity));
			}
			else work.Delta.Put(work.Tables.Catalog, oldKey, work.Bytes(old.Descriptor.Length + 32L, () => NativeStateCodec.Catalog(old with { References = old.References - 1 })));
		}
		ulong result = 0;
		if (hasFacts)
		{
			var identity = ClassIdentity(collection, descriptor);
			result = work.Registry(NativeRegistryKind.Class, identity, false);
			if (result != 0)
			{
				var key = work.Pair(collection, result);
				var old = work.Read(work.Tables.Catalog, key, NativeStateCodec.Catalog) ?? throw new InvalidDataException("Active class lookup without catalog row.");
				if (!old.Descriptor.AsSpan().SequenceEqual(descriptor) || old.References < 1) throw new InvalidDataException("Class identity mismatch.");
				var updated = old with { References = checked(old.References + 1) };
				work.Delta.Put(work.Tables.Catalog, key, work.Bytes(descriptor.Length + 32L, () => NativeStateCodec.Catalog(updated)));
			}
			else
			{
				result = work.Registry(NativeRegistryKind.Class, identity);
				var entry = new NativeCatalogEntry(1, descriptor);
				var totals = Totals(collection);
				counts[collection] = (checked(totals.Count + 1), checked(totals.Bytes + MetadataBytes(result, identity, entry)));
				work.Delta.Put(work.Tables.Catalog, work.Pair(collection, result), work.Bytes(descriptor.Length + 32L, () => NativeStateCodec.Catalog(entry)));
			}
		}
		foreach (var (id, totals) in counts)
		{
			work.Budget.CancellationToken.ThrowIfCancellationRequested();
			if (totals.Count > MaximumClasses) throw new NativeCapacityException("catalog-classes");
			if (totals.Bytes > MaximumBytes) throw new NativeCapacityException("catalog-bytes");
			work.Delta.Put(work.Tables.Metadata, work.Key(30, id), work.Number(totals.Count));
			work.Delta.Put(work.Tables.Metadata, work.Key(31, id), work.Number(totals.Bytes));
		}
		return result;
	}
}
