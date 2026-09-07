using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using SharpMUSH.Database.Memgraph;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database.Memgraph;

/// <summary>
/// World backup for the Memgraph provider: a logical capture of the whole graph, and — the part that
/// makes it a backup rather than a file — a restore that puts it back.
///
/// <para>Runs only on the memgraph leg of the matrix, following the same gate as the other
/// provider-specific tests: the Memgraph container is started only when
/// <c>SHARPMUSH_DATABASE_PROVIDER</c> selects it.</para>
///
/// <para>Staging, retention and the <c>latest</c> pointer are <c>WorldBackupWriter</c>'s and are
/// covered once, against Lightning, rather than re-tested per provider.</para>
/// </summary>
// These tests empty and rewrite the whole graph, and share one instance of it, so they must take
// turns: run in parallel, the churn test's writes land in the middle of the restore test's capture
// and the restore comes back with nothing.
[NotInParallel]
public class BackupTests
{
	// A container this class owns, NOT the session-shared one. These tests empty the graph and put a
	// capture back, which is the whole point of a restore test and is exactly what you must never do
	// to a database the rest of the suite is using.
	[ClassDataSource<BackupMemgraphServer>(Shared = SharedType.PerClass)]
	public required BackupMemgraphServer Memgraph { get; init; }

	private static bool MemgraphSelected =>
		string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"), "memgraph",
			StringComparison.OrdinalIgnoreCase);

	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-memgraph-" + Guid.NewGuid().ToString("N"));

	private static void Delete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort.
		}
	}

	/// <summary>Non-null whenever <see cref="MemgraphSelected"/> is true, which every test checks first.</summary>
	private IDriver Driver => Memgraph.Driver!;

	[Test]
	public async Task ACaptureRestoresIntoAnEmptiedGraphCarryingTheSameNodesAndEdges()
	{
		if (!MemgraphSelected) return;

		var root = TempPath();
		var driver = Driver;
		try
		{
			await using (var seed = driver.AsyncSession())
			{
				await seed.RunAsync("MATCH (n) DETACH DELETE n");
				await seed.RunAsync(
					"CREATE (a:Thing {name: 'God'})-[:LOCATED_IN {since: 1}]->(b:Room {name: 'Room Zero'})");
			}

			var backups = new MemgraphWorldBackupService(driver,
				new WorldBackupOptions { Root = root }, NullLogger<MemgraphWorldBackupService>.Instance);

			var result = await backups.CreateAsync();
			await Assert.That(result.IsT0).IsTrue();

			var json = await File.ReadAllTextAsync(
				Path.Combine(result.AsT0.Path, MemgraphWorldBackupService.ExportFileName));
			var captured = MemgraphStagingDatabase.Deserialize(json);
			await Assert.That(captured).IsNotNull();
			// Asserted before the restore so a failure says which half is at fault.
			await Assert.That(captured!.Nodes.Count).IsEqualTo(2);
			await Assert.That(captured.Relationships.Count).IsEqualTo(1);

			// Wipe, then put the capture back — the half that makes this a backup.
			await using (var wipe = driver.AsyncSession())
			{
				await wipe.RunAsync("MATCH (n) DETACH DELETE n");
			}

			await MemgraphStagingDatabase.RestoreAsync(driver, captured!, NullLogger<BackupTests>.Instance);

			await using var check = driver.AsyncSession();
			var restored = await check.RunAsync(
				"MATCH (a:Thing)-[r:LOCATED_IN]->(b:Room) RETURN a.name AS a, b.name AS b, r.since AS since");
			var rows = await restored.ToListAsync();

			await Assert.That(rows.Count).IsEqualTo(1);
			await Assert.That(rows[0]["a"].As<string>()).IsEqualTo("God");
			await Assert.That(rows[0]["b"].As<string>()).IsEqualTo("Room Zero");
			await Assert.That(rows[0]["since"].As<long>()).IsEqualTo(1);
		}
		finally
		{
			Delete(root);
		}
	}

	/// <summary>
	/// The capture reads nodes and relationships in one transaction. As two, a write landing between
	/// them yields relationships whose endpoints are missing from the capture — a dump that cannot
	/// restore, produced with no error at the time. This asserts the invariant that guarantees it can:
	/// every edge endpoint is present among the captured nodes, while the graph is being written to
	/// throughout.
	/// </summary>
	[Test]
	public async Task ACaptureTakenDuringConcurrentWritesHasNoDanglingEdges()
	{
		if (!MemgraphSelected) return;

		var driver = Driver;
		await using (var seed = driver.AsyncSession())
		{
			await seed.RunAsync("MATCH (n) DETACH DELETE n");
		}

		using var writing = new CancellationTokenSource();
		var writer = Task.Run(async () =>
		{
			await using var session = driver.AsyncSession();
			while (!writing.IsCancellationRequested)
			{
				// Each write adds a node AND an edge, so a capture that reads the two halves at
				// different moments has a real chance of seeing one without the other.
				await session.RunAsync(
					"CREATE (a:Churn {tag: 'x'})-[:LINKS {n: 1}]->(b:Churn {tag: 'y'})");
			}
		}, writing.Token);

		try
		{
			for (var attempt = 0; attempt < 10; attempt++)
			{
				var captured = await MemgraphStagingDatabase.CaptureAsync(
					driver, NullLogger<BackupTests>.Instance);

				var nodeIds = captured.Nodes.Select(n => n.BackupId).ToHashSet();
				var dangling = captured.Relationships
					.Where(r => !nodeIds.Contains(r.StartNodeBackupId) || !nodeIds.Contains(r.EndNodeBackupId))
					.ToArray();

				await Assert.That(dangling.Length).IsEqualTo(0)
					.Because($"attempt {attempt} captured {dangling.Length} edges whose endpoints are missing");
			}
		}
		finally
		{
			await writing.CancelAsync();
			try
			{
				await writer;
			}
			catch (OperationCanceledException)
			{
				// Expected.
			}

			await using var cleanup = driver.AsyncSession();
			await cleanup.RunAsync("MATCH (n) DETACH DELETE n");
		}
	}

	[Test]
	public async Task TheProviderReportsBackupsAsSupported()
	{
		if (!MemgraphSelected) return;

		var root = TempPath();
		var driver = Driver;
		var backups = new MemgraphWorldBackupService(driver,
			new WorldBackupOptions { Root = root, Keep = 4 }, NullLogger<MemgraphWorldBackupService>.Instance);

		await Assert.That(backups.IsSupported).IsTrue();
		await Assert.That(backups.UnavailableReason).IsEmpty();
		await Assert.That(backups.Keep).IsEqualTo(4);
	}
}
