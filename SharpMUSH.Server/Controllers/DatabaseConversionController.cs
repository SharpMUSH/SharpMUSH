using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ServerAdmin)]
public class DatabaseConversionController(ImportSessionRegistry sessions, ImportPromotionJournal journal,
	MushCnfImportService configuration, IStorageCapacityService capacity, IAuditLog audit,
	IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DatabaseConversionController> logger) : ControllerBase
{
	private const long MaxUploadFileSize = 100 * 1024 * 1024;
	private const long MaxConfigFileSize = 1024 * 1024;
	private const long MultipartOverhead = 1024 * 1024;

	/// <summary>Uploads immutable private inputs for an isolated preview; this action never applies live configuration or promotes a world.</summary>
	[HttpPost("upload")]
	[RequestSizeLimit(4 * MaxUploadFileSize + MaxConfigFileSize + MultipartOverhead)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxUploadFileSize)]
	public async Task<ActionResult<string>> UploadDatabase([FromForm] IFormFile file, [FromForm] IFormFile? mailFile,
		[FromForm] IFormFile? chatFile, [FromForm] IFormFile? configFile, [FromForm] IFormFile? mailFolderFile,
		[FromForm] string? format, CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0) return BadRequest("No file uploaded");
		if (!DatabaseImportFormats.TryParse(format, out var importFormat)) return BadRequest("Unknown database format.");
		if (configFile is { Length: > MaxConfigFileSize }) return BadRequest("The configuration file is larger than 1 MB.");
		var inputs = new[] { file, mailFile, chatFile, mailFolderFile }.OfType<IFormFile>().ToArray();
		if (inputs.Any(input => input.Length > MaxUploadFileSize)) return BadRequest("An import input is larger than 100 MB.");
		if (capacity.Measure().ImportShortfall(inputs.Sum(input => input.Length), isolated: true) is { Length: > 0 } shortfall)
			return StatusCode(StatusCodes.Status507InsufficientStorage, shortfall);
		var directory = Path.Combine(journal.SessionsPath, ".upload-" + Guid.NewGuid().ToString("N"));
		ImportPromotionJournal.PrivateDirectory(directory);
		var started = false;
		try
		{
			async Task<string?> SaveAsync(IFormFile? input, string name)
			{
				if (input is null) return null;
				var path = Path.Combine(directory, name);
				await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				await input.CopyToAsync(output, cancellationToken);
				return path;
			}
			var files = new DatabaseImportFiles((await SaveAsync(file, "world"))!, await SaveAsync(mailFile, "mail"),
				await SaveAsync(chatFile, "chat"), await SaveAsync(mailFolderFile, "folders"), importFormat);
			string? text = null;
			if (configFile is not null)
			{
				using var reader = new StreamReader(configFile.OpenReadStream());
				text = await reader.ReadToEndAsync(cancellationToken);
			}
			var id = sessions.Start(files, text, await configuration.SnapshotAsync(cancellationToken), directory,
				clock.GetUtcNow(), Path.GetFileName(file.FileName));
			started = true;
			logger.LogInformation("Import preview {SessionId} started by account {AccountId}", id, User.FindFirstValue(ClaimTypes.NameIdentifier));
			return Ok(new { sessionId = id, message = "Isolated import preview started; promotion requires a separate restart confirmation." });
		}
		catch (InvalidOperationException)
		{
			return Conflict("Another preview or restart operation is active; finish or abort it first.");
		}
		catch (OperationCanceledException)
		{
			return StatusCode(StatusCodes.Status499ClientClosedRequest, "Upload cancelled before preview started.");
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
		{
			logger.LogWarning("Import upload was refused: {ErrorType}", error.GetType().Name);
			return BadRequest("The import upload could not be prepared.");
		}
		finally { if (!started) Directory.Delete(directory, recursive: true); }
	}

	[HttpGet("progress/{sessionId}")]
	public ActionResult<ConversionProgress> GetProgress(string sessionId)
		=> sessions.Progress(sessionId) is ConversionProgress progress ? Ok(progress) : NotFound("Session not found");

	[HttpGet("result/{sessionId}")]
	public ActionResult<ConversionResult> GetResult(string sessionId)
	{
		if (sessions.Get(sessionId) is not ImportSession session || session.Result is not { } result
			|| session.State is ImportSessionState.Preparing or ImportSessionState.Converting or ImportSessionState.Validating)
			return NotFound("Session not found or not completed");
		return Ok(result with { ImportSessionId = session.Id, RequiresPromotion = session.State == ImportSessionState.Ready });
	}

	[HttpGet("sessions")]
	public ActionResult<IReadOnlyList<ImportSessionStatus>> ListSessions()
		=> Ok(journal.List().Select(session => new ImportSessionStatus(session.Id, session.State.ToString(), session.Result, session.Failure,
			session.Started, session.SourceFile)).ToArray());

	[HttpGet("previous")]
	public ActionResult<PreviousWorldStatus> GetPreviousWorld() => Ok(new PreviousWorldStatus(journal.RetainsPreviousWorld));

	public sealed record DiscardConfirmation(bool ConfirmDiscard);

	/// <summary>Deletes the world the last promotion replaced, which the next promotion needs out of the way.</summary>
	[HttpPost("previous/discard")]
	public async Task<ActionResult> DiscardPreviousWorld(DiscardConfirmation confirmation)
	{
		if (!confirmation.ConfirmDiscard) return BadRequest("Discarding the previous world requires explicit confirmation.");
		try
		{
			journal.DiscardPrevious();
		}
		catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
		{
			return Conflict("The previous world cannot be discarded: " + error.Message);
		}
		await RecordAuditAsync("previous", "database.import.discard-previous", "The world the last import replaced was deleted.");
		return Ok(new PreviousWorldStatus(false));
	}

	[HttpGet("status/{sessionId}")]
	public ActionResult GetStatus(string sessionId)
		=> sessions.Get(sessionId) is ImportSession session
			? Ok(new { sessionId, state = session.State.ToString(), result = session.Result, failure = session.Failure })
			: NotFound("Session not found");

	[HttpPost("cancel/{sessionId}")]
	public ActionResult CancelConversion(string sessionId)
	{
		try { return sessions.Abort(sessionId) ? Ok(new { message = "Preview cancellation requested." }) : Conflict("This session is committed or awaiting restart; it cannot be aborted."); }
		catch (IOException) { return Conflict("The session worker is still active; retry once its current operation finishes."); }
	}

	public sealed record RestartConfirmation(bool ConfirmRestart);

	/// <summary>Separate authorization and explicit intent: preview/upload permission alone never commits a world.</summary>
	[HttpPost("promote/{sessionId}")]
	public async Task<ActionResult> Promote(string sessionId, RestartConfirmation confirmation, CancellationToken cancellationToken)
	{
		if (!confirmation.ConfirmRestart) return BadRequest("Promotion requires explicit confirmation that all connections will close and the server must restart.");
		try
		{
			journal.RequestPromotion(sessionId, cancellationToken);
			Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
			await RecordAuditAsync(sessionId, "database.import.promote", "Validated preview approved; process restart requested.");
			return Accepted(new { sessionId, state = "restartRequired" });
		}
		catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
		{ return Conflict("The preview cannot be promoted: " + error.Message); }
	}

	[HttpPost("rollback/{sessionId}")]
	public async Task<ActionResult> Rollback(string sessionId, RestartConfirmation confirmation, CancellationToken cancellationToken)
	{
		if (!confirmation.ConfirmRestart) return BadRequest("Rollback requires explicit restart confirmation.");
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			journal.RequestRollback(sessionId);
			Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
			await RecordAuditAsync(sessionId, "database.import.rollback", "Retained matching world/key-ring rollback approved; restart requested.");
			return Accepted(new { sessionId, state = "restartRequired" });
		}
		catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
		{ return Conflict("The retained world cannot be restored: " + error.Message); }
	}

	private async Task RecordAuditAsync(string sessionId, string action, string details)
	{
		try
		{
			await audit.RecordPortalAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", action,
				new AuditTarget("database-import", sessionId, "Database import"), details, CancellationToken.None);
		}
		catch (Exception)
		{
			// IAuditLog is best-effort after the action, which has already happened.
			logger.LogWarning("Import audit {Action} failed; the action itself stands.", action);
		}
	}
}
