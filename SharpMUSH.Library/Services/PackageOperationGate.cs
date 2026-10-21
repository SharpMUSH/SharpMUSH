using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageOperationGate : IPackageOperationGate, IPackageReadIsolationGate
{
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <summary>
	/// Whether the current async flow holds the gate. Set inside <see cref="RunAsync{T}"/>, so it flows
	/// into the operation and whatever it awaits, and never back out to the caller.
	/// </summary>
	private readonly AsyncLocal<bool> _held = new();
	private readonly AsyncLocal<bool> _isolatedRead = new();

	/// <inheritdoc />
	public Task<T> RunIsolatedReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(read);
		if (_held.Value)
		{
			throw new InvalidOperationException("An isolated package read cannot enter an unfinished operation or read.");
		}
		return RunAsync(async () =>
		{
			_isolatedRead.Value = true;
			try { return await read(); }
			finally { _isolatedRead.Value = false; }
		}, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
	{
		if (_isolatedRead.Value)
		{
			throw new InvalidOperationException("A package operation cannot start inside an isolated package read.");
		}
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
