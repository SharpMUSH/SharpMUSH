using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.DatabaseConversion;
using System.Collections.Concurrent;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabaseConversionController(
	IPennMUSHDatabaseConverter converter,
	ISharpDatabase database,
	ILogger<DatabaseConversionController> logger)
	: ControllerBase
{
	/// <summary>
	/// Upload and convert a PennMUSH database file
	/// </summary>
	[HttpPost("upload")]
	[RequestSizeLimit(104857600)] // 100 MB
	public async Task<ActionResult<string>> UploadDatabase([FromForm] IFormFile file, CancellationToken cancellationToken)
	{
		if (file == null || file.Length == 0)
		{
			return BadRequest("No file uploaded");
		}

		try
		{
			// Create a temporary file to store the uploaded database
			var tempPath = Path.Combine(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.db");

			await using (var stream = System.IO.File.Create(tempPath))
			{
				await file.CopyToAsync(stream, cancellationToken);
			}

			logger.LogInformation("Uploaded PennMUSH database file: {FileName} ({Size} bytes)", file.FileName, file.Length);

			// Start conversion in background and return a session ID
			var sessionId = Guid.NewGuid().ToString();

			// Store the conversion task in a static dictionary for progress tracking
			DatabaseConversionSession.StartConversion(sessionId, converter, tempPath, logger, cancellationToken);

			return Ok(new { sessionId, message = "Conversion started" });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error uploading PennMUSH database file");
			return StatusCode(500, $"Error uploading file: {ex.Message}");
		}
	}

	/// <summary>
	/// Get the progress of a conversion session
	/// </summary>
	[HttpGet("progress/{sessionId}")]
	public ActionResult<ConversionProgress?> GetProgress(string sessionId)
	{
		var progress = DatabaseConversionSession.GetProgress(sessionId);
		if (progress == null)
		{
			return NotFound("Session not found");
		}

		return Ok(progress);
	}

	/// <summary>
	/// Get the result of a completed conversion session
	/// </summary>
	[HttpGet("result/{sessionId}")]
	public async Task<ActionResult<ConversionResult?>> GetResult(string sessionId)
	{
		var result = await DatabaseConversionSession.GetResult(sessionId);
		if (result == null)
		{
			return NotFound("Session not found or not completed");
		}

		return Ok(result);
	}

	/// <summary>
	/// Cancel a conversion session
	/// </summary>
	[HttpPost("cancel/{sessionId}")]
	public ActionResult CancelConversion(string sessionId)
	{
		var cancelled = DatabaseConversionSession.CancelConversion(sessionId);
		if (!cancelled)
		{
			return NotFound("Session not found or already completed");
		}

		return Ok(new { message = "Conversion cancelled" });
	}

	/// <summary>
	/// Safely import a PennMUSH database using staging: imports into a staging database first,
	/// then promotes it to live only on success. On failure, the live database is untouched.
	/// </summary>
	[HttpPost("wipe-and-import")]
	[RequestSizeLimit(104857600)] // 100 MB
	public async Task<ActionResult<object>> WipeAndImportDatabase(
		[FromForm] IFormFile databaseFile,
		[FromForm] IFormFile configFile,
		CancellationToken cancellationToken)
	{
		if (databaseFile == null || databaseFile.Length == 0)
		{
			return BadRequest("No database file uploaded");
		}

		if (configFile == null || configFile.Length == 0)
		{
			return BadRequest("No config file uploaded");
		}

		try
		{
			// Step 1: Save uploaded files to temp paths
			var configTempPath = Path.Combine(Path.GetTempPath(), $"pennmush_config_{Guid.NewGuid()}.cnf");
			using (var configReader = new StreamReader(configFile.OpenReadStream()))
			{
				var configContent = await configReader.ReadToEndAsync(cancellationToken);
				await System.IO.File.WriteAllTextAsync(configTempPath, configContent, cancellationToken);
			}

			var dbTempPath = Path.Combine(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.db");
			await using (var stream = System.IO.File.Create(dbTempPath))
			{
				await databaseFile.CopyToAsync(stream, cancellationToken);
			}

			logger.LogInformation("Uploaded PennMUSH files: db={DbFile} ({DbSize} bytes), config={CnfFile} ({CnfSize} bytes)",
				databaseFile.FileName, databaseFile.Length, configFile.FileName, configFile.Length);

			// Step 2: Create staging database
			logger.LogWarning("Admin initiated staged wipe-and-import");
			var staging = await database.CreateStagingAsync(cancellationToken);

			// Step 3: Start conversion in background with staging database
			var sessionId = Guid.NewGuid().ToString();
			DatabaseConversionSession.StartStagedConversion(
				sessionId, converter, staging, dbTempPath, configTempPath, logger, cancellationToken);

			return Ok(new { sessionId, message = "Staging database created. Import started." });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during wipe-and-import setup");
			return StatusCode(500, $"Error during wipe-and-import: {ex.Message}");
		}
	}

	/// <summary>
	/// Promote a successful staged import to the live database.
	/// </summary>
	[HttpPost("promote/{sessionId}")]
	public async Task<ActionResult<object>> PromoteStaging(string sessionId, CancellationToken cancellationToken)
	{
		var result = DatabaseConversionSession.GetStagingContext(sessionId);
		if (result == null)
		{
			return NotFound("Session not found or has no staging context");
		}

		var (staging, conversionResult) = result.Value;
		if (conversionResult == null)
		{
			return BadRequest("Import has not completed yet");
		}

		if (!conversionResult.IsSuccessful)
		{
			return BadRequest("Cannot promote a failed import. Abort instead.");
		}

		try
		{
			await staging.PromoteToLiveAsync(cancellationToken);
			DatabaseConversionSession.CleanupSession(sessionId);
			logger.LogInformation("Staging import promoted to live database for session {SessionId}", sessionId);
			return Ok(new { message = "Staging database promoted to live successfully." });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error promoting staging database for session {SessionId}", sessionId);
			return StatusCode(500, $"Error promoting: {ex.Message}");
		}
	}

	/// <summary>
	/// Abort a staged import, discarding the staging database.
	/// </summary>
	[HttpPost("abort/{sessionId}")]
	public async Task<ActionResult<object>> AbortStaging(string sessionId, CancellationToken cancellationToken)
	{
		var result = DatabaseConversionSession.GetStagingContext(sessionId);
		if (result == null)
		{
			return NotFound("Session not found or has no staging context");
		}

		var (staging, _) = result.Value;

		try
		{
			await staging.DisposeAsync();
			DatabaseConversionSession.CleanupSession(sessionId);
			logger.LogInformation("Staging import aborted and cleaned up for session {SessionId}", sessionId);
			return Ok(new { message = "Staging database discarded. Live database unchanged." });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error aborting staging for session {SessionId}", sessionId);
			return StatusCode(500, $"Error aborting: {ex.Message}");
		}
	}
}

/// <summary>
/// Static class to track conversion sessions
/// </summary>
public static class DatabaseConversionSession
{
	private static readonly ConcurrentDictionary<string, SessionData> _sessions = new();

	private class SessionData
	{
		public Task<ConversionResult>? ConversionTask { get; set; }
		public ConversionProgress? CurrentProgress { get; set; }
		public ConversionResult? Result { get; set; }
		public CancellationTokenSource CancellationSource { get; set; } = new();
		public string TempFilePath { get; set; } = string.Empty;
		public IStagingDatabase? Staging { get; set; }
	}

	public static void StartConversion(
		string sessionId,
		IPennMUSHDatabaseConverter converter,
		string tempFilePath,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var sessionData = new SessionData
		{
			TempFilePath = tempFilePath
		};

		var progress = new Progress<ConversionProgress>(p =>
		{
			if (_sessions.TryGetValue(sessionId, out var session))
			{
				session.CurrentProgress = p;
			}
		});

		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			sessionData.CancellationSource.Token);

		sessionData.ConversionTask = converter.ConvertDatabaseAsync(tempFilePath, progress, targetDatabase: null, linkedCts.Token)
			.ContinueWith(task =>
			{
				try
				{
					if (task.IsCompletedSuccessfully)
					{
						var result = task.Result;
						if (_sessions.TryGetValue(sessionId, out var session))
						{
							session.Result = result;
						}

						// Clean up temp file
						try
						{
							if (File.Exists(tempFilePath))
							{
								File.Delete(tempFilePath);
							}
						}
						catch (Exception ex)
						{
							logger.LogWarning(ex, "Failed to delete temporary file: {Path}", tempFilePath);
						}

						return result;
					}
					else if (task.IsFaulted)
					{
						logger.LogError(task.Exception?.InnerException, "Error during conversion");
						// Preserve the original exception by throwing the inner exception
						if (task.Exception?.InnerException != null)
						{
							throw task.Exception.InnerException;
						}
						throw new InvalidOperationException("Conversion failed with unknown error");
					}
					else
					{
						throw new OperationCanceledException("Conversion was cancelled");
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error processing conversion result");
					throw;
				}
			}, TaskScheduler.Default);

		_sessions[sessionId] = sessionData;

		// Schedule cleanup of old sessions after 1 hour using a background timer
		// Use CancellationToken.None to ensure cleanup runs even if request is cancelled
		_ = Task.Delay(TimeSpan.FromHours(1), CancellationToken.None)
			.ContinueWith(_ =>
			{
				try
				{
					_sessions.TryRemove(sessionId, out var removedSession);

					// Dispose of cancellation token source
					removedSession?.CancellationSource?.Dispose();

					// Clean up any remaining temp files
					if (removedSession != null && File.Exists(removedSession.TempFilePath))
					{
						try
						{
							File.Delete(removedSession.TempFilePath);
						}
						catch (IOException ex)
						{
							logger.LogWarning(ex, "Failed to delete temp file during cleanup: {Path}", removedSession.TempFilePath);
						}
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error during session cleanup for {SessionId}", sessionId);
				}
			}, TaskScheduler.Default);
	}

	public static ConversionProgress? GetProgress(string sessionId)
	{
		return _sessions.TryGetValue(sessionId, out var session)
			? session.CurrentProgress
			: null;
	}

	public static async Task<ConversionResult?> GetResult(string sessionId)
	{
		if (!_sessions.TryGetValue(sessionId, out var session))
		{
			return null;
		}

		if (session.Result != null)
		{
			return session.Result;
		}

		var task = session.ConversionTask;
		if (task == null || !task.IsCompleted)
		{
			return null;
		}

		try
		{
			return await task;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static bool CancelConversion(string sessionId)
	{
		if (!_sessions.TryGetValue(sessionId, out var session))
		{
			return false;
		}

		if (session.ConversionTask?.IsCompleted == true)
		{
			return false;
		}

		session.CancellationSource.Cancel();

		// Clean up temp file - ignore specific expected exceptions during cleanup
		try
		{
			if (File.Exists(session.TempFilePath))
			{
				File.Delete(session.TempFilePath);
			}
		}
		catch (IOException)
		{
			// File is in use or access denied - expected during cancellation, ignore
		}
		catch (UnauthorizedAccessException)
		{
			// No permission to delete - expected, ignore
		}

		return true;
	}

	public static void StartStagedConversion(
		string sessionId,
		IPennMUSHDatabaseConverter converter,
		IStagingDatabase staging,
		string dbTempFilePath,
		string configTempFilePath,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var sessionData = new SessionData
		{
			TempFilePath = dbTempFilePath,
			Staging = staging
		};

		var progress = new Progress<ConversionProgress>(p =>
		{
			if (_sessions.TryGetValue(sessionId, out var session))
			{
				session.CurrentProgress = p;
			}
		});

		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			sessionData.CancellationSource.Token);

		sessionData.ConversionTask = Task.Run(async () =>
		{
			try
			{
				// Step 1: Migrate the staging database schema
				await staging.Migrate(linkedCts.Token);
				logger.LogInformation("Staging database migrated successfully");

				// Step 2: Apply PennMUSH config to staging
				try
				{
					var importedOptions = SharpMUSH.Configuration.ReadPennMushConfig.Create(configTempFilePath);
					await staging.SetExpandedServerData(nameof(SharpMUSH.Configuration.Options.SharpMUSHOptions), importedOptions);
					logger.LogInformation("PennMUSH config applied to staging database");
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to parse/apply PennMUSH config — continuing with defaults");
				}

				// Step 3: Run the PennMUSH database conversion into staging
				var result = await converter.ConvertDatabaseAsync(
					dbTempFilePath, progress, staging, linkedCts.Token);

				if (_sessions.TryGetValue(sessionId, out var session))
				{
					session.Result = result;
				}

				// Clean up temp files
				TryDeleteFile(dbTempFilePath, logger);
				TryDeleteFile(configTempFilePath, logger);

				return result;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Error during staged conversion");
				// On failure, dispose staging to clean up
				try { await staging.DisposeAsync(); }
				catch (Exception disposeEx) { logger.LogWarning(disposeEx, "Error cleaning up staging after failure"); }
				throw;
			}
		}, linkedCts.Token);

		_sessions[sessionId] = sessionData;

		// Schedule cleanup after 1 hour
		_ = Task.Delay(TimeSpan.FromHours(1), CancellationToken.None)
			.ContinueWith(async _ =>
			{
				if (_sessions.TryRemove(sessionId, out var removedSession))
				{
					removedSession.CancellationSource.Dispose();
					if (removedSession.Staging != null)
					{
						try { await removedSession.Staging.DisposeAsync(); }
						catch { /* best effort */ }
					}
					TryDeleteFile(removedSession.TempFilePath, logger);
				}
			}, TaskScheduler.Default);
	}

	public static (IStagingDatabase staging, ConversionResult? result)? GetStagingContext(string sessionId)
	{
		if (!_sessions.TryGetValue(sessionId, out var session) || session.Staging == null)
		{
			return null;
		}
		return (session.Staging, session.Result);
	}

	public static void CleanupSession(string sessionId)
	{
		if (_sessions.TryRemove(sessionId, out var session))
		{
			session.CancellationSource.Dispose();
		}
	}

	private static void TryDeleteFile(string path, ILogger logger)
	{
		try
		{
			if (File.Exists(path)) File.Delete(path);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
		}
	}
}
