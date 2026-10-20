using System.Buffers.Binary;
using LightningDB;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	// This marker is an invalidation history, never evidence that a lifecycle has settled.
	private static ReadOnlySpan<byte> FixturePackageStateKey => "prototype.semantic.package-settlement/v1"u8;
	private sealed record FixturePackageWriteTracking(SemanticFixtureCapability Owner);
	private FixturePackageWriteTracking? _fixturePackageTracking;

	internal void AttachFixturePackageTracking(SemanticFixtureCapability owner)
	{
		if (_fixturePackageTracking is not null) throw new InvalidOperationException("Fixture tracking is already attached.");
		_fixturePackageTracking = new(owner);
	}

	private void RefuseUnclassifiedFixtureWrite()
	{
		if (_fixturePackageTracking is not { } tracking) return;
		_ = tracking.Owner.AdmitPackageMutation();
		throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
	}

	private static void EncodeUnknownPackageState(Span<byte> target, Guid identity, ulong generation)
	{
		target.Clear();
		"FPKG"u8.CopyTo(target);
		target[4] = 1;
		identity.TryWriteBytes(target.Slice(8, 16), bigEndian: true, out _);
		BinaryPrimitives.WriteUInt64BigEndian(target[24..], generation);
	}

	private static ulong ReadFixturePackageGeneration(LightningTransaction transaction, LmdbDb meta, Guid identity)
	{
		var (code, _, value) = transaction.Get(meta, FixturePackageStateKey);
		if (!FoundNative(code, "read fixture package state") || Tx.CheckedLength(value) != 32)
			throw SemanticFixtureCapability.Unavailable();
		var bytes = value.AsSpan();
		if (!bytes[..4].SequenceEqual("FPKG"u8) || bytes[4] != 1 || bytes[5] > 1
			|| bytes[6] != 0 || bytes[7] != 0 || new Guid(bytes.Slice(8, 16), bigEndian: true) != identity)
			throw SemanticFixtureCapability.Unavailable();
		return BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]);
	}

	private sealed partial class Tx
	{
		private bool _fixtureWritePoisoned;
		private bool _fixturePackageInvalidated;
		internal void RequireFixtureWriteNotPoisoned()
		{
			if (_fixtureWritePoisoned) throw SemanticFixtureCapability.Unavailable();
		}
		private void BeforeFixtureMutation(TableDef table, ReadOnlySpan<byte> key, bool prefixDelete = false)
		{
			if (fixturePackageTracking is not { } tracking) return;
			RequireFixtureWriteNotPoisoned();
			try
			{
				if (table.Name == Tables.Meta.Name && (Protects(FixturePackageStateKey, key, prefixDelete)
					|| Protects("prototype.semantic.fixture.identity/v1"u8, key, prefixDelete)))
					throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
				if (_fixturePackageInvalidated || !(table.Name == "pkg" || table.Name.StartsWith("pkg.", StringComparison.Ordinal))) return;
				if (!tracking.Owner.AdmitPackageMutation()) throw SemanticFixtureCapability.Unavailable();
				var meta = Db(Tables.Meta);
				var identity = tracking.Owner.FixtureId;
				if (!TryReadFixtureIdentity(tx, meta, out var actual) || actual != identity)
					throw SemanticFixtureCapability.Unavailable();
				var generation = checked(ReadFixturePackageGeneration(tx, meta, identity) + 1);
				Span<byte> state = stackalloc byte[32];
				EncodeUnknownPackageState(state, identity, generation);
				var code = tx.Put(meta, FixturePackageStateKey, state);
				if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "invalidate fixture package state");
				_fixturePackageInvalidated = true;
			}
			catch { _fixtureWritePoisoned = true; throw; }
		}
		private static bool Protects(ReadOnlySpan<byte> protectedKey, ReadOnlySpan<byte> key, bool prefixDelete)
			=> prefixDelete ? protectedKey.StartsWith(key) : protectedKey.SequenceEqual(key);
	}
}
