using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>Owns one disposable world lineage; actual-host projection activation is explicit.</summary>
public sealed class SemanticWikiPrototypeFixture : IAsyncDisposable
{
	private readonly object _disposeGate = new();
	private bool _disposed;

	public SemanticWikiPrototypeFixture(string ownedScratchRoot) : this(SemanticFixtureCapability.IssueNew(ownedScratchRoot)) { }
	private SemanticWikiPrototypeFixture(SemanticFixtureCapability capability) => Capability = capability;

	/// <summary>Recovery tests only: the world a killed process issued, adopted once that process has exited.</summary>
	internal static SemanticWikiPrototypeFixture AdoptAfterOwnerExit(string worldPath) => new(SemanticFixtureCapability.AdoptAfterOwnerExit(worldPath));

	internal SemanticFixtureCapability Capability { get; }
	public string WorldPath => Capability.CanonicalPath;
	public Guid FixtureId => Capability.FixtureId;
	public bool HasQuarantinedArtifacts => Capability.IsQuarantined;
	public string CleanupDiagnostic => HasQuarantinedArtifacts
		? $"Fixture native closure is uncertain; retained owned world and reservation: {WorldPath}"
		: string.Empty;

	public LightningDatabase OpenProvider(ILogger<LightningDatabase> logger, LightningStoreOptions options,
		IPasswordService passwordService, IObjectRelationLoader? relations,
		IReadOnlyList<IMigrationSource> migrationSources, IReadOnlyList<PluginFlag> pluginFlags,
		AccountCredentialGate credentialGate, Lazy<IEnumerable<IAccountCredentialRevocation>> credentialRevokers)
		=> LightningDatabase.OpenSemanticFixture(Capability, logger, options, passwordService, relations,
			migrationSources, pluginFlags, credentialGate, credentialRevokers);

	public ValueTask DisposeAsync()
	{
		lock (_disposeGate)
		{
			if (_disposed) return ValueTask.CompletedTask;
			Capability.Dispose();
			if (Capability.MayRemoveArtifacts)
			{
				SemanticFixtureCapability.RejectLinkedAncestors(WorldPath);
				if (Directory.Exists(WorldPath)) Directory.Delete(WorldPath, true);
			}
			else System.Diagnostics.Trace.TraceWarning(CleanupDiagnostic);
			_disposed = true;
		}
		return ValueTask.CompletedTask;
	}
}
