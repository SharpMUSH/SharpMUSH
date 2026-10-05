namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The one process-wide lock package operations take (#1484). Apply, upgrade, uninstall, rollback
/// and the profile-handler reset each read the registry, plan and then write; two of them
/// interleaving can pair one operation's baselines with the other's softcode. Each holds this from
/// its first registry read to its last write, so they run one at a time.
///
/// <para>An in-process lock is enough because the engine is one process
/// (<c>docs/design/engine-data-trunk.md</c>, single-process assumptions).</para>
/// </summary>
public interface IPackageOperationGate
{
	/// <summary>
	/// Runs <paramref name="operation"/> holding the gate, waiting for it first if another operation
	/// holds it. Reentrant within one async flow: an operation that is already inside the gate (the
	/// profile-handler reset installing the bundled package, say) runs a nested one straight through
	/// instead of waiting for itself.
	/// </summary>
	Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
}
