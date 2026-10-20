using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private readonly object _fixtureDisposeGate = new();
	private SemanticFixtureLease? _fixtureLease;
	private bool _fixtureClosed;
	private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _fixtureCloseFailure;

	internal static LightningDatabase OpenSemanticFixture(SemanticFixtureCapability capability,
		ILogger<LightningDatabase> logger, LightningStoreOptions options, IPasswordService passwordService,
		IObjectRelationLoader? relations, IReadOnlyList<IMigrationSource> migrationSources,
		IReadOnlyList<PluginFlag> pluginFlags, AccountCredentialGate credentialGate,
		Lazy<IEnumerable<IAccountCredentialRevocation>> credentialRevokers)
	{
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(passwordService);
		ArgumentNullException.ThrowIfNull(migrationSources);
		ArgumentNullException.ThrowIfNull(pluginFlags);
		ArgumentNullException.ThrowIfNull(credentialGate);
		ArgumentNullException.ThrowIfNull(credentialRevokers);
		// These known managed option errors precede native acquisition in Store's existing constructor.
		// Reject them before Claim so a corrected first attempt remains possible.
		if (options.MaxBatch < 1 || options.ReaderCheckInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(options));
		var lease = capability.Claim(options);
		try
		{
			var provider = new LightningDatabase(logger, options, passwordService, relations, migrationSources,
				pluginFlags, credentialGate, credentialRevokers)
			{ _fixtureLease = lease };
			provider.Store.AttachFixturePackageTracking(capability);
			try { if (capability.Phase == SemanticFixturePhase.Active) provider.Store.RegisterExistingAtomicLayout(); }
			catch { provider.DisposeAsync().GetAwaiter().GetResult(); throw; }
			return provider;
		}
		catch
		{
			// Store.Open may have acquired native resources without returning an owner. Never infer
			// closure from a null provider, release this lease, or delete its world in this case.
			lease.Quarantine();
			throw;
		}
	}

	internal ValueTask InitializeSemanticFixtureIdentityAsync(CancellationToken ct)
	{
		if (_fixtureLease is not { } lease) return ValueTask.CompletedTask;
		ct.ThrowIfCancellationRequested();
		Store.BindFixtureIdentity(lease.FixtureId, lease.IsFirstOpen, ct);
		return ValueTask.CompletedTask;
	}

	private void DisposeSemanticFixture(SemanticFixtureLease lease)
	{
		lock (_fixtureDisposeGate)
		{
			_fixtureCloseFailure?.Throw();
			if (_fixtureClosed) return;
			try { Store.Dispose(); }
			catch (Exception error)
			{
				_fixtureCloseFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
				lease.Quarantine();
				throw;
			}
			_fixtureClosed = true;
			lease.Dispose();
		}
	}
}
