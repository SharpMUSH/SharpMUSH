namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Optional access to the existing package-operation gate for an observation that must not run
/// inside an unfinished operation. Implementations must share the operation gate's exclusion.
/// This proves exclusion only; it does not certify recovery from an earlier failed operation.
/// </summary>
public interface IPackageReadIsolationGate
{
	/// <summary>
	/// Waits for package operations to finish, then keeps them excluded until the callback finishes.
	/// Rejects entry from an operation or isolated read in the same async flow. The callback must not
	/// start a package operation. Cancellation while waiting never invokes the callback; cancellation
	/// after entry does not release the gate until the callback has actually completed.
	/// </summary>
	Task<T> RunIsolatedReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken = default);
}
