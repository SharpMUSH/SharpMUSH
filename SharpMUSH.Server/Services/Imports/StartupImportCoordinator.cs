using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services.Imports;

public enum StartupImportState { NotConfigured, AlreadyApplied, RestartRequested, Failed }
public sealed record StartupImportOutcome(StartupImportState State, string? SessionId = null, string? Failure = null);

/// <summary>Uses the same preview/commit contract as the portal; a content receipt prevents reimporting on every restart.</summary>
public sealed class StartupImportCoordinator(ImportPreviewCoordinator previews, ImportPromotionJournal journal,
	ILogger<StartupImportCoordinator> logger)
{
	public async Task<StartupImportOutcome> PrepareAsync(DatabaseImportFiles files, string? configuration = null,
		ConfigurationSnapshot? baseline = null, PortalAdministrator? administrator = null, CancellationToken cancellationToken = default)
	{
		try
		{
			var fingerprint = await FingerprintAsync(files, configuration, cancellationToken);
			if (journal.AppliedStartupFingerprint == fingerprint) return new(StartupImportState.AlreadyApplied);
			var preview = await previews.PreviewAsync(files, configuration, baseline, administrator, cancellationToken);
			if (preview.State != ImportSessionState.Ready)
				return new(StartupImportState.Failed, preview.Id, preview.Failure ?? "Startup import did not validate.");
			journal.Save(preview with { StartupFingerprint = fingerprint });
			journal.RequestPromotion(preview.Id, cancellationToken);
			logger.LogWarning("Startup import {SessionId} is validated; a process restart is required to install its world and configuration", preview.Id);
			return new(StartupImportState.RestartRequested, preview.Id);
		}
		catch (Exception error)
		{
			logger.LogError("Startup import was not committed: {ErrorType}", error.GetType().Name);
			return new(StartupImportState.Failed, Failure: "Startup import failed: " + error.GetType().Name);
		}
	}

	private static async Task<string> FingerprintAsync(DatabaseImportFiles files, string? configuration, CancellationToken ct)
	{
		var hashes = new List<string> { files.Format.ToString() };
		foreach (var source in new[] { files.Database, files.Mail, files.Chat, files.MailFolders })
		{
			if (source is null) { hashes.Add(""); continue; }
			await using var input = File.OpenRead(source);
			hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(input, ct)));
		}
		hashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration ?? ""))));
		return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hashes)));
	}
}
