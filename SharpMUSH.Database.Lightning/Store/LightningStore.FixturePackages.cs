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
	internal static readonly byte[] FixturePackageStateKeyBytes = FixturePackageStateKey.ToArray();
	private sealed record FixturePackageWriteTracking(SemanticFixtureLease Lease)
	{
		internal SemanticFixtureCapability Owner => Lease.Owner;
	}
	private FixturePackageWriteTracking? _fixturePackageTracking;

	internal void AttachFixturePackageTracking(SemanticFixtureLease lease)
	{
		if (_fixturePackageTracking is not null) throw new InvalidOperationException("Fixture tracking is already attached.");
		_fixturePackageTracking = new(lease);
	}

	/// <summary>
	/// Captures, in the submitting flow, the root package operation that flow belongs to, so each job in a
	/// group commit attributes its own package-state bump. Ordinary stores pay nothing.
	/// </summary>
	private Func<ITx, T> AttributeFixtureWrite<T>(Func<ITx, T> job)
	{
		if (_fixturePackageTracking is null) return job;
		var submitter = SemanticFixturePackageOperation.Ambient;
		return tx =>
		{
			((Tx)tx).PackageSubmitter = submitter;
			return job(tx);
		};
	}

	// A conservative attempt fence: successful return proves only a committed UNKNOWN marker.
	// The writer owns completion through commit; cancellation never detaches an in-flight transaction.
	internal ValueTask<ulong> BeginFixturePackageAttemptAsync(SemanticFixtureLease lease, CancellationToken ct,
		SemanticFixturePackageOperation? attribution = null)
	{
		if (_fixturePackageTracking is not { } tracking || !ReferenceEquals(tracking.Lease, lease))
			throw SemanticFixtureCapability.Unavailable();
		tracking.Owner.RequirePackageAttempt(lease);
		ct.ThrowIfCancellationRequested();
		return _writer.EnqueueRawAsync(tx =>
		{
			// The raw writer has acquired this store's native lifetime gate before this recheck.
			tracking.Owner.RequirePackageAttempt(lease);
			ct.ThrowIfCancellationRequested();
			var meta = _tables[Tables.Meta];
			if (!Tx.TryReadFixtureIdentity(tx, meta, out var actual) || actual != lease.FixtureId)
				throw SemanticFixtureCapability.Unavailable();
			var previous = ReadFixturePackageGeneration(tx, meta, lease.FixtureId);
			var generation = checked(previous + 1);
			Span<byte> state = stackalloc byte[32];
			EncodeUnknownPackageState(state, lease.FixtureId, generation);
			var code = tx.Put(meta, FixturePackageStateKey, state);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "begin fixture package attempt");
			ct.ThrowIfCancellationRequested();
			tracking.Owner.RecordPackageBump(lease, attribution, previous, generation);
			Commit(tx);
			return generation;
		}, ct);
	}

	/// <summary>
	/// Marks the current generation SETTLED for <paramref name="root"/>, in one writer transaction, only when
	/// the marker is still UNKNOWN at exactly that root's expected generation and the capability agrees the
	/// root may settle it. False, writing nothing, otherwise. Only the package coordinator calls this, after
	/// it has joined the operation's completion and observation.
	/// </summary>
	internal ValueTask<bool> SettleFixturePackageAttemptAsync(SemanticFixtureLease lease, SemanticFixturePackageOperation root)
	{
		if (_fixturePackageTracking is not { } tracking || !ReferenceEquals(tracking.Lease, lease))
			throw SemanticFixtureCapability.Unavailable();
		return _writer.EnqueueRawAsync(tx =>
		{
			var meta = _tables[Tables.Meta];
			if (!Tx.TryReadFixtureIdentity(tx, meta, out var actual) || actual != lease.FixtureId) return false;
			var (found, _, value) = tx.Get(meta, FixturePackageStateKey);
			if (!FoundNative(found, "read fixture package state") || Tx.CheckedLength(value) != 32) return false;
			var current = value.AsSpan();
			if (current[5] != 0) return false;
			var generation = ReadFixturePackageGeneration(tx, meta, lease.FixtureId);
			if (!tracking.Owner.MaySettlePackageOperation(lease, root, generation)) return false;
			Span<byte> state = stackalloc byte[32];
			EncodeUnknownPackageState(state, lease.FixtureId, generation);
			state[5] = 1;
			var code = tx.Put(meta, FixturePackageStateKey, state);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "settle fixture package attempt");
			Commit(tx);
			return true;
		}, CancellationToken.None);
	}

	/// <summary>
	/// Activation's own certificate: when no package attempt has ever been made (the marker is exactly UNKNOWN
	/// at generation 0), the empty package history is settled. Any other marker is left unchanged. Activation
	/// already refuses a fixture with any admitted package history, and an active fixture refuses package writes.
	/// </summary>
	internal static void CertifyEmptyFixturePackageHistory(ITx tx, Guid identity)
	{
		var raw = ((Tx)tx).Native;
		var meta = ((Tx)tx).MetaHandle;
		var (found, _, value) = raw.Get(meta, FixturePackageStateKey);
		if (!FoundNative(found, "read fixture package state") || Tx.CheckedLength(value) != 32) return;
		Span<byte> unknown = stackalloc byte[32];
		EncodeUnknownPackageState(unknown, identity, 0);
		if (!value.AsSpan().SequenceEqual(unknown)) return;
		unknown[5] = 1;
		var code = raw.Put(meta, FixturePackageStateKey, unknown);
		if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "certify fixture package history");
	}

	/// <summary>
	/// What a reader may conclude from the package marker it read in its own snapshot: SETTLED only for a
	/// well-formed version-1 marker of <paramref name="identity"/> whose state is settled; anything else,
	/// missing or malformed included, is UNKNOWN.
	/// </summary>
	internal static bool IsSettledFixturePackageState(ReadOnlySpan<byte> marker, Guid identity)
		=> marker.Length == 32 && marker[..4].SequenceEqual("FPKG"u8) && marker[4] == 1 && marker[5] == 1
			&& marker[6] == 0 && marker[7] == 0 && new Guid(marker.Slice(8, 16), bigEndian: true) == identity;

	private void RefuseUnclassifiedFixtureWrite()
	{
		if (_fixturePackageTracking is not { } tracking) return;
		tracking.Owner.RequireCurrentMutationLease(tracking.Lease);
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
		internal LightningTransaction Native => tx;
		internal LmdbDb MetaHandle => Db(Tables.Meta);
		/// <summary>The root package operation whose flow submitted this job; null for none.</summary>
		internal SemanticFixturePackageOperation? PackageSubmitter { get; set; }
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
					|| Protects("prototype.semantic.fixture.identity/v1"u8, key, prefixDelete) || IsFixtureVersionKey(key, prefixDelete)))
					throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
				if (ClassifyFixtureTable(table.Name) is { } version) AdvanceFixtureVersion(version);
				if (_fixturePackageInvalidated || !(table.Name == "pkg" || table.Name.StartsWith("pkg.", StringComparison.Ordinal))) return;
				tracking.Owner.RequireCurrentMutationLease(tracking.Lease);
				if (!tracking.Owner.AdmitPackageMutation()) throw SemanticFixtureCapability.Unavailable();
				var meta = Db(Tables.Meta);
				var identity = tracking.Owner.FixtureId;
				if (!TryReadFixtureIdentity(tx, meta, out var actual) || actual != identity)
					throw SemanticFixtureCapability.Unavailable();
				var previous = ReadFixturePackageGeneration(tx, meta, identity);
				var generation = checked(previous + 1);
				Span<byte> state = stackalloc byte[32];
				EncodeUnknownPackageState(state, identity, generation);
				var code = tx.Put(meta, FixturePackageStateKey, state);
				if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "invalidate fixture package state");
				tracking.Owner.RecordPackageBump(tracking.Lease, PackageSubmitter, previous, generation);
				_fixturePackageInvalidated = true;
			}
			catch { _fixtureWritePoisoned = true; throw; }
		}
		private static bool Protects(ReadOnlySpan<byte> protectedKey, ReadOnlySpan<byte> key, bool prefixDelete)
			=> prefixDelete ? protectedKey.StartsWith(key) : protectedKey.SequenceEqual(key);
	}
}
