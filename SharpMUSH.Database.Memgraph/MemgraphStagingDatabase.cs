using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Database.Memgraph;

/// <summary>
/// A staging implementation for Memgraph (single-database system).
/// Since Memgraph cannot create parallel databases, this implementation:
/// 1. Snapshots all current nodes and relationships to a JSON backup file
/// 2. Wipes the database and re-migrates (the staging IS the live database)
/// 3. On Abort: restores from the backup
/// 4. On Promote: deletes the backup file
/// </summary>
public sealed class MemgraphStagingDatabase : MemgraphDatabase, IStagingDatabase
{
	private readonly IDriver _driver;
	private readonly ILogger _logger;
	private readonly string _backupPath;
	private bool _aborted;

	public string StagingId { get; }
	public bool IsPromoted { get; private set; }

	public MemgraphStagingDatabase(
		ILogger<MemgraphDatabase> logger,
		IDriver driver,
		IPasswordService passwordService,
		string backupPath,
		string stagingId)
		: base(logger, driver, passwordService)
	{
		_driver = driver;
		_logger = logger;
		_backupPath = backupPath;
		StagingId = stagingId;
	}

	public Task PromoteToLiveAsync(CancellationToken ct = default)
	{
		if (IsPromoted) throw new InvalidOperationException("Staging already promoted.");
		if (_aborted) throw new InvalidOperationException("Staging was aborted.");

		// Delete the backup file — we're committing to the new data
		if (File.Exists(_backupPath))
		{
			File.Delete(_backupPath);
			_logger.LogInformation("Memgraph staging promoted — backup deleted: {Path}", _backupPath);
		}

		IsPromoted = true;
		return Task.CompletedTask;
	}

	public async Task AbortAsync(CancellationToken ct = default)
	{
		if (IsPromoted || _aborted) return;
		_aborted = true;

		if (!File.Exists(_backupPath))
		{
			_logger.LogError("Cannot abort staging — backup file not found: {Path}", _backupPath);
			return;
		}

		_logger.LogWarning("Aborting staging import — restoring database from backup");

		// Wipe current state
		await using var session = _driver.AsyncSession();
		await session.RunAsync("MATCH (n) DETACH DELETE n");

		// Restore from backup
		var backupJson = await File.ReadAllTextAsync(_backupPath, ct);
		var backup = JsonSerializer.Deserialize<MemgraphBackup>(backupJson);

		if (backup is null)
		{
			_logger.LogError("Backup file was empty or corrupt");
			return;
		}

		// Restore nodes
		foreach (var node in backup.Nodes)
		{
			var labels = string.Join(":", node.Labels.Select(l => $"`{l}`"));
			var propsJson = JsonSerializer.Serialize(node.Properties);
			await session.RunAsync(
				$"CREATE (n:{labels}) SET n = $props",
				new { props = node.Properties });
		}

		// Restore relationships
		foreach (var rel in backup.Relationships)
		{
			await session.RunAsync(
				$"MATCH (a), (b) WHERE elementId(a) = $startId AND elementId(b) = $endId " +
				$"CREATE (a)-[r:`{rel.Type}`]->(b) SET r = $props",
				new { startId = rel.StartNodeElementId, endId = rel.EndNodeElementId, props = rel.Properties });
		}

		// Clean up backup file
		File.Delete(_backupPath);

		_logger.LogInformation("Database restored from backup successfully");
	}

	public async ValueTask DisposeAsync()
	{
		if (!IsPromoted && !_aborted)
		{
			await AbortAsync();
		}
	}

	/// <summary>
	/// Creates a backup of all nodes and relationships in the current Memgraph database.
	/// </summary>
	public static async Task<string> BackupCurrentDatabaseAsync(
		IDriver driver, ILogger logger, CancellationToken ct = default)
	{
		logger.LogInformation("Creating Memgraph database backup...");

		var backup = new MemgraphBackup();

		await using var session = driver.AsyncSession();

		// Export all nodes
		var nodeResult = await session.RunAsync(
			"MATCH (n) RETURN labels(n) as labels, elementId(n) as eid, properties(n) as props");
		var nodeRecords = await nodeResult.ToListAsync();
		foreach (var record in nodeRecords)
		{
			backup.Nodes.Add(new BackupNode
			{
				ElementId = record["eid"].As<string>(),
				Labels = record["labels"].As<List<string>>(),
				Properties = record["props"].As<Dictionary<string, object>>()
			});
		}

		// Export all relationships
		var relResult = await session.RunAsync(
			"MATCH (a)-[r]->(b) RETURN type(r) as type, elementId(a) as startEid, elementId(b) as endEid, properties(r) as props");
		var relRecords = await relResult.ToListAsync();
		foreach (var record in relRecords)
		{
			backup.Relationships.Add(new BackupRelationship
			{
				Type = record["type"].As<string>(),
				StartNodeElementId = record["startEid"].As<string>(),
				EndNodeElementId = record["endEid"].As<string>(),
				Properties = record["props"].As<Dictionary<string, object>>()
			});
		}

		var backupPath = Path.Combine(Path.GetTempPath(), $"sharpmush_memgraph_backup_{Guid.NewGuid():N}.json");
		var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = false });
		await File.WriteAllTextAsync(backupPath, json, ct);

		logger.LogInformation("Memgraph backup created: {Path} ({NodeCount} nodes, {RelCount} rels)",
			backupPath, backup.Nodes.Count, backup.Relationships.Count);

		return backupPath;
	}
}

public class MemgraphBackup
{
	public List<BackupNode> Nodes { get; set; } = [];
	public List<BackupRelationship> Relationships { get; set; } = [];
}

public class BackupNode
{
	public string ElementId { get; set; } = "";
	public List<string> Labels { get; set; } = [];
	public Dictionary<string, object> Properties { get; set; } = [];
}

public class BackupRelationship
{
	public string Type { get; set; } = "";
	public string StartNodeElementId { get; set; } = "";
	public string EndNodeElementId { get; set; } = "";
	public Dictionary<string, object> Properties { get; set; } = [];
}
