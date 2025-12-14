using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that handles PennMUSH database conversion on startup if configured.
/// </summary>
public class PennMUSHDatabaseConversionService : BackgroundService
{
	private readonly IPennMUSHDatabaseConverter _converter;
	private readonly IOptions<SharpMUSHOptions> _options;
	private readonly ILogger<PennMUSHDatabaseConversionService> _logger;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly bool _stopOnFailure;
	private readonly string? _databaseFilePath;

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
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Check if conversion is enabled via configuration
		if (string.IsNullOrWhiteSpace(_databaseFilePath))
		{
			_logger.LogInformation("PennMUSH database conversion not configured (PENNMUSH_DATABASE_PATH not set)");
			return;
		}

		if (!File.Exists(_databaseFilePath))
		{
			_logger.LogError("PennMUSH database file not found: {FilePath}", _databaseFilePath);
			return;
		}

		_logger.LogInformation("Starting PennMUSH database conversion from: {FilePath}", _databaseFilePath);

		try
		{
			var result = await _converter.ConvertDatabaseAsync(_databaseFilePath, stoppingToken);

			if (result.IsSuccessful)
			{
				_logger.LogInformation(
					"PennMUSH database conversion completed successfully in {Duration}. " +
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
			}
			else
			{
				_logger.LogError(
					"PennMUSH database conversion failed with {ErrorCount} errors",
					result.Errors.Count);

				foreach (var error in result.Errors.Take(10))
				{
					_logger.LogError("  {Error}", error);
				}
				if (result.Errors.Count > 10)
				{
					_logger.LogError("  ... and {Count} more errors", result.Errors.Count - 10);
				}

				// Optionally stop the application on conversion failure
				if (_stopOnFailure)
				{
					_logger.LogCritical("Stopping application due to conversion failure");
					_lifetime.StopApplication();
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Fatal error during PennMUSH database conversion");

			// Optionally stop the application on conversion exception
			if (_stopOnFailure)
			{
				_logger.LogCritical("Stopping application due to conversion exception");
				_lifetime.StopApplication();
			}
		}
	}
}
