using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Database.SurrealDB;

/// <summary>
/// World backup for the SurrealDB provider, over the client's own <c>Export</c> — which works on the
/// embedded RocksDB engine this provider runs in production, not only against a remote server. The
/// result is a SurrealQL script (<c>OPTION IMPORT; DEFINE TABLE …; INSERT […]</c>) that recreates the
/// world, written as <c>world.surql</c>.
///
/// <para>Unlike the Lightning provider's copy, this is a <b>logical</b> export rather than a
/// page-level one, so it is only as consistent as the export itself: the game keeps running while it
/// is produced, and SurrealDB does not document it as a point-in-time snapshot. Take it as a copy
/// that restores to a coherent-looking world, not as a guaranteed instant. Quiescing the game around
/// a backup is the way to be certain, and that is worth knowing before a restore, not during
/// one.</para>
///
/// <para>Staging, the <c>latest</c> pointer and retention are <see cref="WorldBackupWriter"/>'s, the
/// same as every other provider.</para>
/// </summary>
public sealed class SurrealWorldBackupService : IWorldBackupService
{
	/// <summary>Name of the export inside each backup directory. The extension is what SurrealDB's own
	/// import expects, so a restore is <c>surreal import</c> against this file.</summary>
	public const string ExportFileName = "world.surql";

	private readonly WorldBackupWriter _writer;

	public SurrealWorldBackupService(
		ISurrealDbClient client,
		WorldBackupOptions options,
		ILogger<SurrealWorldBackupService> logger)
		=> _writer = new WorldBackupWriter(options, async (directory, ct) =>
		{
			var script = await client.Export(cancellationToken: ct);
			await File.WriteAllTextAsync(Path.Combine(directory, ExportFileName), script ?? string.Empty, ct);
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
