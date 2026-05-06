using Core.Arango;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

/// <summary>
/// A staging ArangoDB database that operates on a separate database name.
/// When promoted, it drops the original database and swaps the handle on the live instance.
/// </summary>
public sealed class ArangoStagingDatabase : ArangoDatabase, IStagingDatabase
{
	private readonly ArangoDatabase _liveDatabase;
	private readonly IArangoContext _arangoContext;
	private readonly ArangoHandle _stagingHandle;
	private readonly ArangoHandle _originalHandle;
	private readonly ILogger _logger;
	private bool _aborted;

	public string StagingId { get; }
	public bool IsPromoted { get; private set; }

	public ArangoStagingDatabase(
		ILogger<ArangoDatabase> logger,
		IArangoContext arangoDb,
		ArangoHandle stagingHandle,
		IMediator mediator,
		IPasswordService passwordService,
		ArangoDatabase liveDatabase,
		ArangoHandle originalHandle,
		string stagingId)
		: base(logger, arangoDb, stagingHandle, mediator, passwordService)
	{
		_liveDatabase = liveDatabase;
		_arangoContext = arangoDb;
		_stagingHandle = stagingHandle;
		_originalHandle = originalHandle;
		_logger = logger;
		StagingId = stagingId;
	}

	public async Task PromoteToLiveAsync(CancellationToken ct = default)
	{
		if (IsPromoted) throw new InvalidOperationException("Staging already promoted.");
		if (_aborted) throw new InvalidOperationException("Staging was aborted.");

		// Drop the original live database
		if (await _arangoContext.Database.ExistAsync(_originalHandle))
		{
			await _arangoContext.Database.DropAsync(_originalHandle);
			_logger.LogInformation("Dropped original database: {OriginalDb}", (string)_originalHandle);
		}

		// Swap the ArangoHandle on the live database instance to point at staging
		SwapHandleOnLive();

		IsPromoted = true;
		_logger.LogInformation("Staging database promoted to live: {StagingDb}", (string)_stagingHandle);
	}

	/// <summary>
	/// Swaps the ArangoHandle on the live database instance to point at this staging database.
	/// Uses reflection since ArangoDatabase uses primary constructor parameters.
	/// </summary>
	private void SwapHandleOnLive()
	{
		var fields = typeof(ArangoDatabase)
			.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
			.Where(f => f.FieldType == typeof(ArangoHandle))
			.ToArray();

		if (fields.Length == 0)
			throw new InvalidOperationException("Cannot find ArangoHandle field on ArangoDatabase to swap.");

		foreach (var field in fields)
		{
			field.SetValue(_liveDatabase, _stagingHandle);
		}
	}

	public async Task AbortAsync(CancellationToken ct = default)
	{
		if (IsPromoted || _aborted) return;
		_aborted = true;

		// Drop the staging database — the live one is untouched
		if (await _arangoContext.Database.ExistAsync(_stagingHandle))
		{
			await _arangoContext.Database.DropAsync(_stagingHandle);
			_logger.LogInformation("Staging database dropped: {StagingDb}", (string)_stagingHandle);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (!IsPromoted && !_aborted)
		{
			await AbortAsync();
		}
	}
}
