using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>The game's options and the object references its last <c>mush.cnf</c> named, as they stood.</summary>
public sealed record ConfigurationSnapshot(SharpMUSHOptions Options, MushCnfObjectReferences References);

/// <summary>
/// Applies an uploaded PennMUSH <c>mush.cnf</c> over the running game's options: what the file names replaces the
/// game's value, what it leaves out keeps it. Also records which objects the file named, so a PennMUSH database
/// imported after it keeps them (<see cref="MushCnfObjectReferences"/>).
/// </summary>
public class MushCnfImportService(
	IConfigOptionWriter config,
	IExpandedDataStore database,
	ILogger<MushCnfImportService> logger)
{
	/// <summary>
	/// Reads <paramref name="content"/>. An uploaded <c>mush.cnf</c> arrives alone: its include lines are not
	/// followed, since they would name files on this server, so the restrict.cnf and alias.cnf it includes are not
	/// read; each line that could not be carried over is logged rather than lost silently.
	/// </summary>
	public async Task<PennMushConfigImport> ReadAsync(string content, CancellationToken cancellationToken = default)
	{
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(path, content, cancellationToken);
			var import = ReadPennMushConfig.Import(path, followIncludes: false);
			foreach (var line in import.Skipped)
			{
				logger.LogWarning("Configuration import did not carry over: {Line}", line);
			}

			return import;
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>Persists <paramref name="import"/> over the current options and tells their readers.</summary>
	public async Task<SharpMUSHOptions> ApplyAsync(PennMushConfigImport import, CancellationToken cancellationToken = default)
	{
		var imported = await config.UpdateAsync(import.Over, cancellationToken);
		await database.SetExpandedServerData(nameof(MushCnfObjectReferences), MushCnfObjectReferences.From(import),
			cancellationToken);
		logger.LogInformation("Configuration imported and persisted successfully");
		return imported;
	}

	/// <summary>What <see cref="RestoreAsync"/> puts back.</summary>
	public async Task<ConfigurationSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
		=> new(await config.CurrentAsync(),
			await database.GetExpandedServerData<MushCnfObjectReferences>(nameof(MushCnfObjectReferences), cancellationToken)
			?? new MushCnfObjectReferences());

	/// <summary>Puts the options and object references back as <paramref name="snapshot"/> had them.</summary>
	public async Task RestoreAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken = default)
	{
		await database.SetExpandedServerData(nameof(MushCnfObjectReferences), snapshot.References, cancellationToken);
		await config.UpdateAsync(_ => snapshot.Options, cancellationToken);
		logger.LogInformation("Configuration restored to what it was before the import");
	}
}
