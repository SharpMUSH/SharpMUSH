using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageOperationGate : IPackageOperationGate
{
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <summary>
	/// Whether the current async flow holds the gate. Set inside <see cref="RunAsync{T}"/>, so it flows
	/// into the operation and whatever it awaits, and never back out to the caller.
	/// </summary>
	private readonly AsyncLocal<bool> _held = new();

	/// <inheritdoc />
	public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
	{
		if (_held.Value)
		{
			return await operation();
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			_held.Value = true;
			return await operation();
		}
		finally
		{
			_held.Value = false;
			_gate.Release();
		}
	}
}
