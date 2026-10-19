using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	// Scalar observation immediately after the first migration writer's guard, before its first write.
	internal Action<SemanticFixturePhase>? AtomicMigrationGuardObserved { get; set; }
	internal int AtomicWikiLegacyMutationsInFlight => _fixtureLease?.Owner.LegacyMutationsInFlight ?? 0;
	internal SemanticFixturePhase AtomicWikiPhase => _fixtureLease?.Owner.Phase ?? SemanticFixturePhase.Inactive;
	private SemanticFixtureMutationLease? EnterFixtureMutation(SemanticFixtureMutation operation)
		=> _fixtureLease?.Owner.EnterMutation(operation);

	private void RequireFixtureMutation(ITx tx, SemanticFixtureMutationLease? admission, NativeReadBudget? sharedBudget = null, NativeReadReservation? lifetime = null)
	{
		if (_fixtureLease is not { } fixture) return;
		if (admission is null || !ReferenceEquals(admission.Owner, fixture.Owner)) throw SemanticFixtureCapability.Unavailable();
		fixture.Owner.RequireMutationPhase(admission);
		try
		{
			var identityBudget = sharedBudget ?? new NativeReadBudget(2, 1024, 4096, CancellationToken.None);
			lifetime?.ReserveAdditional(1024);
			if (((IBoundedReadTx)tx).ReadValue(Tables.Meta, Keys.Str(LightningStore.FixtureIdentityKey), identityBudget) is not NativeReadValue identity)
				throw SemanticFixtureCapability.Unavailable();
			using (identity)
				if (identity.Value.Length != 16 || new Guid(identity.Value, bigEndian: true) != fixture.FixtureId)
					throw SemanticFixtureCapability.Unavailable();
			var marker = ReadActivation(tx, fixture.FixtureId, sharedBudget, lifetime);
			if (admission.Phase == SemanticFixturePhase.Inactive)
			{
				if (marker is not null) throw SemanticFixtureCapability.Unavailable();
			}
			else
			{
				if (marker is not { Active: true }) throw SemanticFixtureCapability.Unavailable();
				var budget = sharedBudget ?? new NativeReadBudget(8, 4096, 64 * 1024, CancellationToken.None);
				var reader = (IBoundedReadTx)tx;
				foreach (var key in new[] { AtomicWikiLayout.RootKey, AtomicWikiLayout.CertificateKey })
				{
					if (reader.ReadValue(AtomicWikiLayout.Metadata, key, budget) is not NativeReadValue value)
						throw SemanticFixtureCapability.Unavailable();
					using (value)
					{
						lifetime?.ReserveAdditional(checked(2048 + 24L * value.Value.Length));
						if (AtomicWikiLayout.Decode(value.Value, fixture.FixtureId) != marker) throw SemanticFixtureCapability.Unavailable();
					}
				}
			}
		}
		catch (NativeReadLimitException error) when (sharedBudget is not null && error.Dimension != "record-bytes") { throw; }
		catch (Exception error) when (error is InvalidDataException or NativeReadLimitException or KeyNotFoundException)
		{
			fixture.Owner.MutationUnavailable();
			throw SemanticFixtureCapability.Unavailable();
		}
		catch (WikiFixtureMutationException error) when (error.Reason == WikiFixtureMutationFailure.Unavailable)
		{
			fixture.Owner.MutationUnavailable();
			throw;
		}
	}

	private ValueTask<T> WriteFixtureAsync<T>(SemanticFixtureMutationLease? admission, Func<ITx, T> job, CancellationToken ct = default)
		=> Store.WriteAsync(tx => { RequireFixtureMutation(tx, admission); return job(tx); }, ct);
	private ValueTask WriteFixtureAsync(SemanticFixtureMutationLease? admission, Action<ITx> job, CancellationToken ct = default)
		=> Store.WriteAsync(tx => { RequireFixtureMutation(tx, admission); job(tx); }, ct);
}
