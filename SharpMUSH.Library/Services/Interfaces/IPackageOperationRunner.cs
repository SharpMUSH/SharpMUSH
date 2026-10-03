using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Runs a portal package operation (apply, rollback, uninstall) as a queue entry (#1332), so that
/// like an HTTP handler command (#1184) it never interleaves with softcode: the queue has one
/// consumer, and a queue entry runs start to finish before the next one starts.
///
/// <para>Before the operation, in the same entry and the same hold of
/// <see cref="IPackageOperationGate"/>, it takes the automatic pre-operation backup (#1333) when the
/// provider supports one and it is not turned off. A backup that fails refuses the operation.</para>
///
/// <para>An apply's lifecycle hooks (<c>AINSTALL</c>, <c>AUPDATE</c>) do not run in the operation's
/// entry. Each is queued as an entry of its own once that entry has finished, and the call returns
/// after they have run. Nothing in the operation's entry waits on the queue, so it cannot deadlock
/// against itself.</para>
/// </summary>
public interface IPackageOperationRunner
{
	/// <param name="operation">A short name for the queue entry and the logs: <c>apply</c>, <c>rollback</c>, <c>uninstall</c>.</param>
	/// <param name="body">The package operation itself.</param>
	/// <param name="cancellationToken">The caller's token, passed on to <paramref name="body"/>.</param>
	Task<PackageOperationOutcome<T>> RunAsync<T>(
		string operation,
		Func<CancellationToken, Task<T>> body,
		CancellationToken cancellationToken = default);
}
