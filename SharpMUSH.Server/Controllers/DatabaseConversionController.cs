using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.DatabaseConversion;
using System.Collections.Concurrent;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ServerAdmin)]
public class DatabaseConversionController(
	IPennMUSHDatabaseConverter converter,
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
			var tempPath = Path.Combine(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.db");

			await using (var stream = System.IO.File.Create(tempPath))
			{
				await file.CopyToAsync(stream, cancellationToken);
			}

			logger.LogInformation("Uploaded PennMUSH database file: {FileName} ({Size} bytes)", file.FileName, file.Length);

			var sessionId = Guid.NewGuid().ToString();

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

		sessionData.ConversionTask = converter.ConvertDatabaseAsync(tempFilePath, progress, linkedCts.Token)
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

		// Use CancellationToken.None to ensure cleanup runs even if request is cancelled
		_ = Task.Delay(TimeSpan.FromHours(1), CancellationToken.None)
			.ContinueWith(_ =>
			{
				try
				{
					_sessions.TryRemove(sessionId, out var removedSession);

					removedSession?.CancellationSource?.Dispose();

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
}
