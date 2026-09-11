using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Owns the session's temporary Lightning storage, shared by all server-host variants.
/// Nested fixture disposal keeps it alive until every consuming host has stopped.
/// Only session-owned temporary storage is removed; caller-supplied paths survive cleanup.
/// </summary>
public sealed class TestDatabaseStorage : IAsyncInitializer, IAsyncDisposable
{
	private string? _ownedPath;

	public Task InitializeAsync()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"), "surrealdb",
				StringComparison.OrdinalIgnoreCase)
			&& Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH") is null)
		{
			_ownedPath = Path.Combine(Path.GetTempPath(), $"sharpmush-lightning-tests-{Guid.NewGuid():N}");
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", _ownedPath);
		}
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _ownedPath, null) is not { } ownedPath)
			return ValueTask.CompletedTask;

		try
		{
			foreach (var path in new[] { ownedPath, ownedPath + ".backups" })
				if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
		}
		finally
		{
			if (Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH") == ownedPath)
				Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", null);
		}
		return ValueTask.CompletedTask;
	}
}
