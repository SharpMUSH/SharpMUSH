using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that imports a database on startup if configured: <c>PENNMUSH_DATABASE_PATH</c> names a
/// PennMUSH outdb, a RhostMUSH flatfile or a TinyMUX flatfile, told apart by its first lines unless
/// <c>PENNMUSH_DATABASE_FORMAT</c> (<c>pennmush</c>, <c>rhostmush</c> or <c>tinymux</c>) says which.
/// </summary>
public class PennMUSHDatabaseConversionService : BackgroundService
{
	private readonly IPennMUSHDatabaseConverter _converter;
	private readonly IOptions<SharpMUSHOptions> _options;
	private readonly ILogger<PennMUSHDatabaseConversionService> _logger;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly bool _stopOnFailure;
	private readonly string? _databaseFilePath;
	private readonly string? _databaseFormat;

	public PennMUSHDatabaseConversionService(
		IPennMUSHDatabaseConverter converter,
		IOptions<SharpMUSHOptions> options,
		ILogger<PennMUSHDatabaseConversionService> logger,
		IHostApplicationLifetime lifetime)
	{
		_converter = converter;
		_options = options;
		_logger = logger;
		_lifetime = lifetime;
		_stopOnFailure = string.Equals(
			Environment.GetEnvironmentVariable("PENNMUSH_CONVERSION_STOP_ON_FAILURE"),
			"true",
			StringComparison.OrdinalIgnoreCase);
		_databaseFilePath = Environment.GetEnvironmentVariable("PENNMUSH_DATABASE_PATH");
		_databaseFormat = Environment.GetEnvironmentVariable("PENNMUSH_DATABASE_FORMAT");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (string.IsNullOrWhiteSpace(_databaseFilePath))
		{
			_logger.LogInformation("Database import not configured (PENNMUSH_DATABASE_PATH not set)");
			return;
		}

		if (!File.Exists(_databaseFilePath))
		{
			_logger.LogError("Database file to import not found: {FilePath}", _databaseFilePath);
			return;
		}

		if (!Enum.TryParse<DatabaseImportFormat>(string.IsNullOrWhiteSpace(_databaseFormat) ? nameof(DatabaseImportFormat.Auto) : _databaseFormat,
				ignoreCase: true, out var format) || !Enum.IsDefined(format))
		{
			_logger.LogError("PENNMUSH_DATABASE_FORMAT is {Format}; it takes auto, pennmush, rhostmush or tinymux", _databaseFormat);
			return;
		}

		_logger.LogInformation("Starting database conversion from: {FilePath}", _databaseFilePath);

		try
		{
			var result = await _converter.ConvertDatabaseAsync(new DatabaseImportFiles(_databaseFilePath, Format: format),
				new Progress<ConversionProgress>(), stoppingToken);

			if (result.IsSuccessful)
			{
				_logger.LogInformation(
					"Database conversion completed successfully in {Duration}. " +
					"Converted: {Players} players, {Rooms} rooms, {Things} things, {Exits} exits, {Attributes} attributes",
					result.Duration,
					result.PlayersConverted,
					result.RoomsConverted,
					result.ThingsConverted,
					result.ExitsConverted,
					result.AttributesConverted);

				if (result.Warnings.Count > 0)
				{
					_logger.LogWarning("Conversion completed with {Count} warnings:", result.Warnings.Count);
					foreach (var warning in result.Warnings.Take(10))
					{
						_logger.LogWarning("  {Warning}", warning);
					}
					if (result.Warnings.Count > 10)
					{
						_logger.LogWarning("  ... and {Count} more warnings", result.Warnings.Count - 10);
					}
				}

				// Every one, not the first ten: an administrator reads this list to check the rewritten code.
				if (result.SoftcodeRewrites.Count > 0)
				{
					_logger.LogWarning("Rewritten softcode, {Count} attribute(s):", result.SoftcodeRewrites.Count);
					foreach (var rewrite in result.SoftcodeRewrites)
					{
						_logger.LogWarning("  {Rewrite}", rewrite);
					}
				}
			}
			else
			{
				_logger.LogError(
					"Database conversion failed with {ErrorCount} errors",
					result.Errors.Count);

				foreach (var error in result.Errors.Take(10))
				{
					_logger.LogError("  {Error}", error);
				}
				if (result.Errors.Count > 10)
				{
					_logger.LogError("  ... and {Count} more errors", result.Errors.Count - 10);
				}

				if (_stopOnFailure)
				{
					_logger.LogCritical("Stopping application due to conversion failure");
					_lifetime.StopApplication();
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Fatal error during database conversion");

			if (_stopOnFailure)
			{
				_logger.LogCritical("Stopping application due to conversion exception");
				_lifetime.StopApplication();
			}
		}
	}
}
