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
	/// <summary>The most either file may be; the admin page allows the same.</summary>
	private const long MaxUploadFileSize = 100 * 1024 * 1024;

	/// <summary>Room for the multipart boundaries and headers around the files.</summary>
	private const long MultipartOverhead = 1024 * 1024;

	/// <summary>
	/// Upload and convert a PennMUSH database file, with its maildb (<c>mailFile</c>) and chatdb
	/// (<c>chatFile</c>) optionally alongside
	/// </summary>
	[HttpPost("upload")]
	[RequestSizeLimit(3 * MaxUploadFileSize + MultipartOverhead)] // All three files at their limit, and the form around them
	[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadFileSize)] // Each file
	public async Task<ActionResult<string>> UploadDatabase([FromForm] IFormFile file, [FromForm] IFormFile? mailFile,
		[FromForm] IFormFile? chatFile, CancellationToken cancellationToken)
	{
		if (file == null || file.Length == 0)
		{
			return BadRequest("No file uploaded");
		}

		var tempPath = Path.Join(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.db");
		string? mailTempPath = null;
		string? chatTempPath = null;
		try
		{

			await using (var stream = System.IO.File.Create(tempPath))
			{
				await file.CopyToAsync(stream, cancellationToken);
			}

			logger.LogInformation("Uploaded PennMUSH database file: {FileName} ({Size} bytes)", file.FileName, file.Length);

			// An empty file is still passed on, so the import reports it rather than dropping it unmentioned.
			if (mailFile is not null)
			{
				mailTempPath = Path.Join(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.maildb");
				await using var mailStream = System.IO.File.Create(mailTempPath);
				await mailFile.CopyToAsync(mailStream, cancellationToken);
				logger.LogInformation("Uploaded PennMUSH mail database file: {FileName} ({Size} bytes)", mailFile.FileName,
					mailFile.Length);
			}

			if (chatFile is not null)
			{
				chatTempPath = Path.Join(Path.GetTempPath(), $"pennmush_{Guid.NewGuid()}.chatdb");
				await using var chatStream = System.IO.File.Create(chatTempPath);
				await chatFile.CopyToAsync(chatStream, cancellationToken);
				logger.LogInformation("Uploaded PennMUSH chat database file: {FileName} ({Size} bytes)", chatFile.FileName,
					chatFile.Length);
			}

			var sessionId = Guid.NewGuid().ToString();

			DatabaseConversionSession.StartConversion(sessionId, converter, tempPath, mailTempPath, chatTempPath, logger,
				cancellationToken);

			return Ok(new { sessionId, message = "Conversion started" });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error uploading PennMUSH database file");
			DatabaseConversionSession.DeleteTempFiles([tempPath, mailTempPath, chatTempPath], logger);
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
		/// <summary>The uploaded database and, when they came with it, the maildb and chatdb.</summary>
		public string?[] TempFilePaths { get; init; } = [];
	}

	/// <summary>Deletes the upload's temporary files; one that is still in use is logged and left.</summary>
	public static void DeleteTempFiles(IEnumerable<string?> paths, ILogger logger)
	{
		foreach (var path in paths)
		{
			try
			{
				if (path is not null && File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				logger.LogWarning(ex, "Failed to delete temporary file: {Path}", path);
			}
		}
	}

	public static void StartConversion(
		string sessionId,
		IPennMUSHDatabaseConverter converter,
		string tempFilePath,
		string? mailTempFilePath,
		string? chatTempFilePath,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var sessionData = new SessionData
		{
			TempFilePaths = [tempFilePath, mailTempFilePath, chatTempFilePath]
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

		sessionData.ConversionTask = converter.ConvertDatabaseAsync(tempFilePath, mailTempFilePath, chatTempFilePath, progress,
				linkedCts.Token)
			.ContinueWith(task =>
			{
				// Every way out — success, fault, cancellation — is done with the uploaded files.
				DeleteTempFiles(sessionData.TempFilePaths, logger);
				try
				{
					if (task.IsCompletedSuccessfully)
					{
						var result = task.Result;
						if (_sessions.TryGetValue(sessionId, out var session))
						{
							session.Result = result;
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

					if (removedSession != null)
					{
						DeleteTempFiles(removedSession.TempFilePaths, logger);
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

		// The conversion's continuation deletes the uploaded files once the import has let go of them.
		session.CancellationSource.Cancel();
		return true;
	}
}
