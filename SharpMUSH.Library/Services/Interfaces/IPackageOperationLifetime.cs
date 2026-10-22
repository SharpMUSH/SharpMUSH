using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Optional capability of the actual package coordinator. Use the same instance as
/// <see cref="IPackageOperationRunner"/>; a second runner would have different sequencing.
/// </summary>
public interface IPackageOperationLifetime
{
	/// <summary>
	/// Waits for sequencing, then returns the retained coordinator's actual completion. Cancellation
	/// while waiting for sequencing throws without starting work. Once sequencing is acquired it asks the body to unwind;
	/// cancelling a caller's observation cannot complete or replace the owned task.
	/// </summary>
	Task<PackageOperationExecution<T>> StartAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null);
}
