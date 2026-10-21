using System.Text;
using LightningDB;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	private static AtomicWikiActivation? RequireAtomicLayout(LightningTransaction tx, LmdbDb meta, Guid expected)
	{
		var marker = ReadFixed(tx, meta, AtomicWikiLayout.MarkerKey);
		var found = 0;
		LmdbDb? metadata = null;
		var opened = new List<LmdbDb>(AtomicWikiLayout.All.Length);
		try
		{
			foreach (var definition in AtomicWikiLayout.All)
			{
				NativeTableName.Validate(definition.Name);
				try
				{
					var table = tx.OpenDatabase(definition.Name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
					opened.Add(table);
					if (table.GetFlags(tx) != DatabaseOpenFlags.None) throw new InvalidOperationException("Invalid semantic table flags.");
					found++;
					if (definition == AtomicWikiLayout.Metadata) metadata = table;
				}
				catch (LightningException error) when (error.StatusCode == (int)MDBResultCode.NotFound)
				{
					// Absence is counted below; opening an existing layout must never create a missing table.
					continue;
				}
			}
			using var root = tx.OpenDatabase();
			using var cursor = tx.CreateCursor(root);
			var prefix = Encoding.UTF8.GetBytes(AtomicWikiLayout.ReservedPrefix);
			var position = cursor.SetRange(prefix);
			var names = 0;
			while (FoundNative(position, "semantic catalog seek/advance"))
			{
				var (code, key, _) = cursor.GetCurrent();
				if (!FoundNative(code, "semantic catalog current")) break;
				var length = Tx.CheckedLength(key);
				if (length > 256) throw new InvalidOperationException("Semantic catalog name exceeds bound.");
				if (!key.AsSpan().StartsWith(prefix)) break;
				if (++names > AtomicWikiLayout.All.Length) throw new InvalidOperationException("Unexpected reserved semantic layout.");
				var name = Encoding.UTF8.GetString(key.AsSpan()).TrimEnd('\0');
				if (!AtomicWikiLayout.All.Any(table => table.Name == name)) throw new InvalidOperationException("Unknown reserved semantic table.");
				position = cursor.Next().resultCode;
			}
			if (marker is null)
			{
				if (found != 0 || names != 0) throw new InvalidOperationException("Semantic layout has no activation marker.");
				return null;
			}
			var state = AtomicWikiLayout.Decode(marker, expected);
			if (!state.Active || found != AtomicWikiLayout.All.Length || names != AtomicWikiLayout.All.Length || metadata is null)
				throw new InvalidOperationException("Semantic activation layout is incomplete.");
			var certificate = ReadFixed(tx, metadata, AtomicWikiLayout.CertificateKey);
			var rootState = ReadFixed(tx, metadata, AtomicWikiLayout.RootKey);
			var schema = ReadFixed(tx, metadata, [1]);
			var sequence = ReadFixed(tx, metadata, [2]);
			if (certificate is null || rootState is null || !marker.AsSpan().SequenceEqual(certificate)
				|| !marker.AsSpan().SequenceEqual(rootState) || schema is null || !schema.AsSpan().SequenceEqual(AtomicWikiLayout.SchemaBytes)
				|| sequence is not { Length: 8 } || System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(sequence) < (ulong)state.Sequence
				|| System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(sequence) > long.MaxValue)
				throw new InvalidOperationException("Semantic activation certificate is missing or incompatible.");
			return state;
		}
		finally { foreach (var table in opened) table.Dispose(); }
	}

	private static byte[]? ReadFixed(LightningTransaction tx, LmdbDb table, byte[] key)
	{
		var (code, _, value) = tx.Get(table, key);
		if (!FoundNative(code, "semantic fixed metadata")) return null;
		if (Tx.CheckedLength(value) > 512) throw new InvalidOperationException("Semantic fixed metadata exceeds bound.");
		return value.AsSpan().ToArray();
	}

	internal void RequireNoAtomicLayout(Guid fixtureId)
		=> ReadNative(tx =>
		{
			if (!Tx.TryReadFixtureIdentity(tx, _tables[Tables.Meta], out var actual) || actual != fixtureId)
				throw new InvalidOperationException("Fixture identity does not match the activation owner.");
			if (RequireAtomicLayout(tx, _tables[Tables.Meta], fixtureId) is not null)
				throw new InvalidOperationException("Fixture projection is already active.");
			return true;
		});

	private T ReadNative<T>(Func<LightningTransaction, T> read)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction(TransactionBeginFlags.ReadOnly);
			return read(tx);
		}
		finally { _gate.ExitReadLock(); }
	}

	internal void RegisterExistingAtomicLayout()
	{
		foreach (var definition in AtomicWikiLayout.All)
			OpenTableOwned(definition.Name, false, null, create: false);
	}

	// Guard and optional epoch contribution run inside the owning raw job, even for an already-open name.
	internal TableDef OpenTableOwned(string name, bool duplicates, Action<ITx, bool>? guard, bool create = true)
	{
		NativeTableName.Validate(name);
		return WriteClassifiedFixtureRaw(tx =>
		{
			var existing = FindTable(name);
			var callback = new Tx(tx, _tableSnapshot, null, _fixturePackageTracking);
			guard?.Invoke(callback, false);
			callback.RequireFixtureWriteNotPoisoned();
			if (existing is not null) { Commit(tx); return existing; }
			var definition = duplicates ? TableDef.Index(name, duplicates: true) : TableDef.Node(name);
			LmdbDb handle;
			try { handle = tx.OpenDatabase(name, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None }); }
			catch (LightningException error) when (create && error.StatusCode == (int)MDBResultCode.NotFound)
			{
				guard?.Invoke(callback, true);
				callback.RequireFixtureWriteNotPoisoned();
				handle = tx.OpenDatabase(name, new DatabaseConfiguration { Flags = FlagsFor(definition) | DatabaseOpenFlags.Create });
			}
			try
			{
				if (handle.GetFlags(tx) != FlagsFor(definition)) throw new InvalidOperationException("Atomic table flags differ from the certified layout.");
				var current = _tableSnapshot;
				var handles = new Dictionary<TableDef, LmdbDb>(current.Handles) { [definition] = handle };
				var names = new Dictionary<string, NativeTable>(current.ByName, StringComparer.Ordinal)
				{ [name] = new NativeTable(definition, handle, handle.GetFlags(tx)) };
				var snapshot = new TableSnapshot(handles, names);
				Commit(tx);
				_tableSnapshot = snapshot;
				_pluginTables = _pluginTables.SetItem(name, definition);
				return definition;
			}
			catch { handle.Dispose(); throw; }
		});
	}
}
