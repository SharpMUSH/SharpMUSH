using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>The portal's process-owned worker lifetimes; reports and ready previews survive restart in the journal.</summary>
public sealed class ImportSessionRegistry(ImportPreviewCoordinator previews, ImportPromotionJournal journal,
	IHostApplicationLifetime lifetime, ILogger<ImportSessionRegistry> logger) : IAsyncDisposable
{
	private readonly ConcurrentDictionary<string, (CancellationTokenSource Cancellation, Task Work)> _running = new();

	public string Start(DatabaseImportFiles files, string? configuration, ConfigurationSnapshot baseline, string uploadDirectory,
		DateTimeOffset? started = null, string? sourceFile = null)
	{
		var session = journal.Create(started, sourceFile);
		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_running.TryAdd(session.Id, (cancellation, completion.Task))) throw new InvalidOperationException("Duplicate import identifier.");
		_ = Task.Run(async () =>
		{
			try { await previews.PreviewSessionAsync(session, files, configuration, baseline, cancellation.Token); }
			catch (Exception error) { logger.LogError("Import {SessionId} worker cleanup failed: {ErrorType}", session.Id, error.GetType().Name); }
			finally
			{
				try { Directory.Delete(uploadDirectory, recursive: true); }
				catch (Exception error) when (error is IOException or UnauthorizedAccessException)
				{ logger.LogWarning("Import upload cleanup retained {SessionId}: {ErrorType}", session.Id, error.GetType().Name); }
				_running.TryRemove(session.Id, out _);
				cancellation.Dispose();
				completion.SetResult();
			}
		});
		return session.Id;
	}

	public Found<ImportSession> Get(string id)
	{
		try
		{
			var session = journal.Get(id);
			if (!_running.ContainsKey(id) && session.State is ImportSessionState.Preparing or ImportSessionState.Converting or ImportSessionState.Validating)
			{
				try
				{
					using var owned = new FileStream(Path.Combine(session.Directory, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
					session = session with
					{
						State = ImportSessionState.Aborted,
						Failure = "Preview worker was interrupted; start a new preview.",
						Result = new ConversionResult { Aborted = true, Errors = ["Preview worker was interrupted; start a new preview."] }
					};
					journal.Save(session);
					ImportPreviewCoordinator.CleanupStaging(session);
				}
				catch (IOException) { /* A surviving orphan worker owns its directory until it finishes. */ }
			}
			return session;
		}
		catch (Exception error) when (error is IOException or ArgumentException) { return new NotFound(); }
	}

	public Found<ConversionProgress> Progress(string id)
	{
		if (Get(id) is not ImportSession session) return new NotFound();
		if (session.State is ImportSessionState.Ready or ImportSessionState.Aborted or ImportSessionState.Committed or ImportSessionState.RolledBack)
			return new ConversionProgress { CurrentPhase = session.State.ToString(), PercentageComplete = 100, RecentErrors = session.Result?.Errors ?? [] };
		try
		{
			return JsonSerializer.Deserialize<ConversionProgress>(File.ReadAllText(Path.Combine(session.Directory, "progress.json")))
				?? new ConversionProgress { CurrentPhase = session.State.ToString() };
		}
		catch (Exception error) when (error is IOException or JsonException)
		{ return new ConversionProgress { CurrentPhase = session.State.ToString() }; }
	}

	public bool Abort(string id)
	{
		if (Get(id) is not ImportSession session) return false;
		if (session.State == ImportSessionState.Ready) return AbortClosedPreview(id);
		if (_running.TryGetValue(id, out var running))
		{
			try { running.Cancellation.Cancel(); }
			catch (ObjectDisposedException) { /* Worker completed between lookup and cancellation. */ }
			if (Get(id) is not ImportSession current) return false;
			return current.State == ImportSessionState.Ready ? AbortClosedPreview(id)
				: current.State is ImportSessionState.Preparing or ImportSessionState.Converting or ImportSessionState.Validating or ImportSessionState.Aborted;
		}
		// A surviving worker keeps ownership until its provider is closed.
		using var owned = new FileStream(Path.Combine(session.Directory, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		return AbortClosedPreview(id);
	}

	private bool AbortClosedPreview(string id)
	{
		if (!journal.AbortPreview(id, "Preview aborted by an administrator.")) return false;
		ImportPreviewCoordinator.CleanupStaging(journal.Get(id));
		return true;
	}

	public async ValueTask DisposeAsync()
	{
		var running = _running.Values.ToArray();
		foreach (var entry in running)
		{
			try { entry.Cancellation.Cancel(); }
			catch (ObjectDisposedException) { /* Worker completed after the shutdown snapshot. */ }
		}
		await Task.WhenAll(running.Select(entry => entry.Work));
	}
}
