using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Database.SurrealDB;

/// <summary>
/// A staging SurrealDB database that operates on a separate database name within the same namespace.
/// On promotion, the original "world" database is dropped and the live instance is swapped to the staging client.
/// </summary>
public sealed class SurrealStagingDatabase : SurrealDatabase, IStagingDatabase
{
	private readonly SurrealDatabase _liveDatabase;
	private readonly ISurrealDbClient _liveClient;
	private readonly ISurrealDbClient _stagingClient;
	private readonly string _stagingDbName;
	private readonly ILogger _logger;
	private bool _aborted;

	public string StagingId { get; }
	public bool IsPromoted { get; private set; }

	public SurrealStagingDatabase(
		ILogger<SurrealDatabase> logger,
		ISurrealDbClient stagingClient,
		IPasswordService passwordService,
		SurrealDatabase liveDatabase,
		ISurrealDbClient liveClient,
		string stagingDbName,
		string stagingId)
		: base(logger, stagingClient, passwordService)
	{
		_liveDatabase = liveDatabase;
		_liveClient = liveClient;
		_stagingClient = stagingClient;
		_stagingDbName = stagingDbName;
		_logger = logger;
		StagingId = stagingId;
	}

	public async Task PromoteToLiveAsync(CancellationToken ct = default)
	{
		if (IsPromoted) throw new InvalidOperationException("Staging already promoted.");
		if (_aborted) throw new InvalidOperationException("Staging was aborted.");

		await _liveClient.RawQuery("REMOVE DATABASE IF EXISTS world;");
		_logger.LogInformation("Dropped original SurrealDB database: world");

		SwapClientOnLive();

		IsPromoted = true;
		_logger.LogInformation("SurrealDB staging database promoted to live: {StagingDb}", _stagingDbName);
	}

	private void SwapClientOnLive()
	{
		var fields = typeof(SurrealDatabase)
			.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
			.Where(f => f.FieldType == typeof(ISurrealDbClient))
			.ToArray();

		if (fields.Length == 0)
			throw new InvalidOperationException("Cannot find ISurrealDbClient field on SurrealDatabase to swap.");

		foreach (var field in fields)
		{
			field.SetValue(_liveDatabase, _stagingClient);
		}
	}

	public async Task AbortAsync(CancellationToken ct = default)
	{
		if (IsPromoted || _aborted) return;
		_aborted = true;

		// Drop the staging database — live is untouched
		await _stagingClient.RawQuery($"REMOVE DATABASE IF EXISTS `{_stagingDbName}`;");
		_logger.LogInformation("SurrealDB staging database dropped: {StagingDb}", _stagingDbName);
	}

	public async ValueTask DisposeAsync()
	{
		if (!IsPromoted && !_aborted)
		{
			await AbortAsync();
		}
	}
}
