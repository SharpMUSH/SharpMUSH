using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Owns the session's temporary Lightning storage, shared by all server-host variants.
/// Nested fixture disposal keeps it alive until every consuming host has stopped.
/// Only session-owned temporary storage is removed; caller-supplied paths survive cleanup.
///
/// <para>The owned world is opened in <c>periodic</c> sync mode unless the run chose one: it is thrown
/// away at the end, so syncing every commit to disk buys nothing, and with the whole suite writing
/// through Lightning's single writer, those syncs were what tests on a slow CI disk sat waiting for.
/// A caller-supplied path keeps the server's durable default.</para>
/// </summary>
public sealed class TestDatabaseStorage : IAsyncInitializer, IAsyncDisposable
{
	private const string SyncVariable = "SHARPMUSH_LIGHTNING_SYNC";

	private string? _ownedPath;
	private bool _ownedSync;

	public Task InitializeAsync()
	{
		if (Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH") is null)
		{
			_ownedPath = Path.Join(Path.GetTempPath(), $"sharpmush-lightning-tests-{Guid.NewGuid():N}");
			Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", _ownedPath);
			if (Environment.GetEnvironmentVariable(SyncVariable) is null)
			{
				_ownedSync = true;
				Environment.SetEnvironmentVariable(SyncVariable, "periodic");
			}
		}
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _ownedPath, null) is not { } ownedPath)
			return ValueTask.CompletedTask;

		try
		{
			foreach (var path in new[] { ownedPath, ownedPath + ".backups" }.Where(Directory.Exists))
				Directory.Delete(path, recursive: true);
		}
		finally
		{
			if (Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH") == ownedPath)
				Environment.SetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH", null);
			if (_ownedSync)
				Environment.SetEnvironmentVariable(SyncVariable, null);
		}
		return ValueTask.CompletedTask;
	}
}
