using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Server.Services;

/// <summary>Startup imports prepare privately before hosted services start; committing requires a second process start.</summary>
public sealed class PennMUSHDatabaseConversionService(StartupImportCoordinator imports, MushCnfImportService configuration,
	ILogger<PennMUSHDatabaseConversionService> logger)
{
	public async Task<StartupImportOutcome> RunBeforeServingAsync(CancellationToken cancellationToken = default)
	{
		var path = Environment.GetEnvironmentVariable("PENNMUSH_DATABASE_PATH");
		if (string.IsNullOrWhiteSpace(path)) return new(StartupImportState.NotConfigured);
		try
		{
			if (!DatabaseImportFormats.TryParse(Environment.GetEnvironmentVariable("PENNMUSH_DATABASE_FORMAT"), out var format))
				return new(StartupImportState.Failed, Failure: "PENNMUSH_DATABASE_FORMAT is invalid.");
			var files = new DatabaseImportFiles(path, Environment.GetEnvironmentVariable("PENNMUSH_MAIL_DATABASE_PATH"),
				Environment.GetEnvironmentVariable("PENNMUSH_CHAT_DATABASE_PATH"), Environment.GetEnvironmentVariable("PENNMUSH_MAIL_FOLDERS_PATH"), format);
			string? text = null;
			if (Environment.GetEnvironmentVariable("PENNMUSH_CONFIG_PATH") is { Length: > 0 } config)
			{
				if (new FileInfo(config).Length > 1024 * 1024) throw new IOException("Startup configuration exceeds 1 MiB.");
				text = await File.ReadAllTextAsync(config, cancellationToken);
			}
			return await imports.PrepareAsync(files, text, await configuration.SnapshotAsync(cancellationToken), cancellationToken);
		}
		catch (Exception error)
		{
			logger.LogError("Startup import failed before serving: {ErrorType}", error.GetType().Name);
			return new(StartupImportState.Failed, Failure: "Startup import failed: " + error.GetType().Name);
		}
	}
}
