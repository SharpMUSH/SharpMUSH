using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Memgraph;

/// <summary>
/// World backup for the Memgraph provider. Memgraph is a database server this process reaches over
/// Bolt, so there is no directory to copy; what it can do is read the whole graph out through that
/// connection, which is the same capture the staging rollback already relies on
/// (<see cref="MemgraphStagingDatabase.CaptureAsync"/>), written as <c>world.json</c>.
///
/// <para>The capture runs in a single read transaction, so nodes and relationships come from one
/// consistent view. It is still a <b>logical</b> dump taken from a running game rather than a
/// page-level snapshot, and it holds a read transaction open for as long as the graph takes to
/// read — worth knowing on a large world.</para>
///
/// <para>Memgraph's own <c>CREATE SNAPSHOT</c> was not used: it writes into Memgraph's data
/// directory inside its own container, where the game cannot apply retention and the deployment's
/// snapshot tool cannot read it. Retention, the <c>latest</c> pointer and staging are
/// <see cref="WorldBackupWriter"/>'s, the same as every other provider.</para>
/// </summary>
public sealed class MemgraphWorldBackupService : IWorldBackupService
{
	/// <summary>Name of the capture inside each backup directory.</summary>
	public const string ExportFileName = "world.json";

	private readonly WorldBackupWriter _writer;

	public MemgraphWorldBackupService(
		IDriver driver,
		WorldBackupOptions options,
		ILogger<MemgraphWorldBackupService> logger)
		=> _writer = new WorldBackupWriter(options, async (directory, ct) =>
		{
			var captured = await MemgraphStagingDatabase.CaptureAsync(driver, logger, ct);
			await File.WriteAllTextAsync(Path.Combine(directory, ExportFileName),
				MemgraphStagingDatabase.Serialize(captured), ct);
		}, logger);

	public bool IsSupported => true;

	public string UnavailableReason => string.Empty;

	public string Root => _writer.Root;

	public int Keep => _writer.Keep;

	public TimeSpan ScheduledInterval => _writer.ScheduledInterval;

	public ValueTask<OneOf<WorldBackup, Error<string>>> CreateAsync(CancellationToken ct = default)
		=> _writer.CreateAsync(ct);

	public IReadOnlyList<WorldBackup> List() => _writer.List();
}
