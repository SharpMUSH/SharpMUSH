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
		IObjectRelationLoader relations,
		string backupPath,
		string stagingId)
		: base(logger, driver, passwordService, relations)
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

		var backupJson = await File.ReadAllTextAsync(_backupPath, ct);
		var backup = JsonSerializer.Deserialize<MemgraphBackup>(backupJson);

		if (backup is null)
		{
			_logger.LogError("Backup file was empty or corrupt");
			return;
		}

		await RestoreAsync(_driver, backup, _logger, ct);

		File.Delete(_backupPath);
	}

	/// <summary>
	/// Replaces everything in the database with <paramref name="backup"/>. This is the other half of
	/// <see cref="CaptureAsync"/>: without it a capture is a file nobody can put back, which is not a
	/// backup. Used both by staging rollback and to restore a world backup.
	///
	/// <para>Destructive — it deletes the current graph first.</para>
	/// </summary>
	public static async Task RestoreAsync(
		IDriver driver, MemgraphBackup backup, ILogger logger, CancellationToken ct = default)
	{
		await using var session = driver.AsyncSession();
		await session.RunAsync("MATCH (n) DETACH DELETE n");

		// Nodes carry their captured id as a temporary property, which is what lets the relationships
		// below find their endpoints again; Memgraph assigns its own ids on create, so the originals
		// cannot be reused directly.
		foreach (var node in backup.Nodes)
		{
			var labels = string.Join(":", node.Labels.Select(l => $"`{EscapeIdentifier(l)}`"));
			await session.RunAsync(
				$"CREATE (n:{labels}) SET n = $props, n._backup_id = $bid",
				new { props = ToDriverValues(node.Properties), bid = node.BackupId });
		}

		foreach (var rel in backup.Relationships)
		{
			await session.RunAsync(
				$"MATCH (a), (b) WHERE a._backup_id = $startId AND b._backup_id = $endId " +
				$"CREATE (a)-[r:`{EscapeIdentifier(rel.Type)}`]->(b) SET r = $props",
				new
				{
					startId = rel.StartNodeBackupId,
					endId = rel.EndNodeBackupId,
					props = ToDriverValues(rel.Properties)
				});
		}

		await session.RunAsync("MATCH (n) WHERE n._backup_id IS NOT NULL REMOVE n._backup_id");

		logger.LogInformation("Database restored from backup: {NodeCount} nodes, {RelCount} rels",
			backup.Nodes.Count, backup.Relationships.Count);
	}

	/// <summary>Reads back what <see cref="Serialize"/> wrote.</summary>
	public static MemgraphBackup? Deserialize(string json) => JsonSerializer.Deserialize<MemgraphBackup>(json);

	/// <summary>
	/// Converts property values that came back from JSON into things the Bolt driver can send.
	///
	/// <para>A capture holds properties as <c>object</c>. Round-tripped through JSON they arrive as
	/// <see cref="JsonElement"/>, which the driver rejects outright — "Cannot understand value with
	/// type System.Text.Json.JsonElement" — so a restore of anything with a non-empty property bag
	/// fails at the first node. Values that never went through JSON pass straight through.</para>
	/// </summary>
	private static Dictionary<string, object?> ToDriverValues(Dictionary<string, object> properties)
		=> properties.ToDictionary(p => p.Key, p => ToDriverValue(p.Value));

	private static object? ToDriverValue(object? value) => value switch
	{
		JsonElement element => element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			// Bolt distinguishes integers from floats, and a property that went in as an integer must
			// come back as one: a dbref or a timestamp read back as 1.0 is a different value.
			JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
			JsonValueKind.Array => element.EnumerateArray().Select(e => ToDriverValue(e)).ToList(),
			JsonValueKind.Object => element.EnumerateObject()
				.ToDictionary(p => p.Name, object? (p) => ToDriverValue(p.Value)),
			_ => element.ToString()
		},
		_ => value
	};

	public async ValueTask DisposeAsync()
	{
		if (!IsPromoted && !_aborted)
		{
			await AbortAsync();
		}
	}

	/// <summary>
	/// Escapes a Cypher identifier (label or relationship type) by replacing backticks with double backticks.
	/// Identifiers are already wrapped in backticks by the caller.
	/// </summary>
	private static string EscapeIdentifier(string identifier) =>
		identifier.Replace("`", "``");

	/// <summary>
	/// Reads every node and relationship out of the database.
	///
	/// <para>Both reads run in <b>one</b> transaction. They must: as two separate queries, a write
	/// landing between them yields a capture whose relationships can reference nodes the capture does
	/// not contain — a dump that fails to restore, produced without any error at the time. One read
	/// transaction gives both queries the same consistent view.</para>
	/// </summary>
	public static async Task<MemgraphBackup> CaptureAsync(
		IDriver driver, ILogger logger, CancellationToken ct = default)
	{
		logger.LogInformation("Capturing Memgraph database contents...");

		await using var session = driver.AsyncSession();

		var backup = await session.ExecuteReadAsync(async tx =>
		{
			var captured = new MemgraphBackup();

			// id() rather than elementId(): it works in both Neo4j and Memgraph.
			var nodeResult = await tx.RunAsync(
				"MATCH (n) RETURN labels(n) as labels, id(n) as nid, properties(n) as props");
			foreach (var record in await nodeResult.ToListAsync())
			{
				captured.Nodes.Add(new BackupNode
				{
					BackupId = record["nid"].As<long>(),
					Labels = record["labels"].As<List<string>>(),
					Properties = record["props"].As<Dictionary<string, object>>()
				});
			}

			var relResult = await tx.RunAsync(
				"MATCH (a)-[r]->(b) RETURN type(r) as type, id(a) as startId, id(b) as endId, properties(r) as props");
			foreach (var record in await relResult.ToListAsync())
			{
				captured.Relationships.Add(new BackupRelationship
				{
					Type = record["type"].As<string>(),
					StartNodeBackupId = record["startId"].As<long>(),
					EndNodeBackupId = record["endId"].As<long>(),
					Properties = record["props"].As<Dictionary<string, object>>()
				});
			}

			return captured;
		});

		logger.LogInformation("Memgraph capture: {NodeCount} nodes, {RelCount} rels",
			backup.Nodes.Count, backup.Relationships.Count);

		return backup;
	}

	/// <summary>
	/// Captures the database to a JSON file in the temp directory, for staging rollback.
	/// </summary>
	public static async Task<string> BackupCurrentDatabaseAsync(
		IDriver driver, ILogger logger, CancellationToken ct = default)
	{
		var backup = await CaptureAsync(driver, logger, ct);

		var backupPath = Path.Combine(Path.GetTempPath(), $"sharpmush_memgraph_backup_{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(backupPath, Serialize(backup), ct);

		logger.LogInformation("Memgraph backup created: {Path}", backupPath);

		return backupPath;
	}

	/// <summary>The on-disk form, shared by staging rollback and world backup so one can restore the other.</summary>
	public static string Serialize(MemgraphBackup backup)
		=> JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = false });
}

public class MemgraphBackup
{
	public List<BackupNode> Nodes { get; set; } = [];
	public List<BackupRelationship> Relationships { get; set; } = [];
}

public class BackupNode
{
	public long BackupId { get; set; }
	public List<string> Labels { get; set; } = [];
	public Dictionary<string, object> Properties { get; set; } = [];
}

public class BackupRelationship
{
	public string Type { get; set; } = "";
	public long StartNodeBackupId { get; set; }
	public long EndNodeBackupId { get; set; }
	public Dictionary<string, object> Properties { get; set; } = [];
}
