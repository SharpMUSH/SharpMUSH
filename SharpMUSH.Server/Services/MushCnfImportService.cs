using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The game's options, the object references its last <c>mush.cnf</c> named, and the secrets a <c>mush.cnf</c> can
/// set, as they stood.
/// </summary>
public sealed record ConfigurationSnapshot(SharpMUSHOptions Options, MushCnfObjectReferences References, SecretSnapshot Secrets);

/// <summary>
/// Applies an uploaded PennMUSH <c>mush.cnf</c> over the running game's options: what the file names replaces the
/// game's value, what it leaves out keeps it. Also records which objects the file named, so a PennMUSH database
/// imported after it keeps them (<see cref="MushCnfObjectReferences"/>). A secret the file names, such as
/// <c>sql_password</c>, goes into the secret store (<see cref="PennMushConfigImport.SecretDirectives"/>), never into
/// the options.
/// </summary>
public class MushCnfImportService(
	IConfigOptionWriter config,
	IExpandedDataStore database,
	ISecretStore secrets,
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

	/// <summary>
	/// Persists <paramref name="import"/> over the current options and tells their readers, and stores its secrets
	/// as set by <paramref name="author"/>.
	/// </summary>
	public async Task<SharpMUSHOptions> ApplyAsync(PennMushConfigImport import, SecretAuthor author,
		CancellationToken cancellationToken = default)
	{
		var imported = await config.UpdateAsync(import.Over, cancellationToken);
		await database.SetExpandedServerData(nameof(MushCnfObjectReferences), MushCnfObjectReferences.From(import),
			cancellationToken);
		foreach (var (name, value) in import.Secrets)
		{
			if (await secrets.SetAsync(name, value, author, cancellationToken) is Error<string> error)
			{
				logger.LogWarning("Configuration import did not store the secret {Secret}: {Reason}", name, error.Value);
			}
		}

		logger.LogInformation("Configuration imported and persisted successfully");
		return imported;
	}

	/// <summary>What <see cref="RestoreAsync"/> puts back.</summary>
	public async Task<ConfigurationSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
		=> new(await config.CurrentAsync(),
			await database.GetExpandedServerData<MushCnfObjectReferences>(nameof(MushCnfObjectReferences), cancellationToken)
			?? new MushCnfObjectReferences(),
			await secrets.SnapshotAsync(PennMushConfigImport.SecretDirectives.Values, cancellationToken));

	/// <summary>Puts the options, object references and secrets back as <paramref name="snapshot"/> had them.</summary>
	public async Task RestoreAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken = default)
	{
		await secrets.RestoreAsync(snapshot.Secrets, cancellationToken);
		await database.SetExpandedServerData(nameof(MushCnfObjectReferences), snapshot.References, cancellationToken);
		await config.UpdateAsync(_ => snapshot.Options, cancellationToken);
		logger.LogInformation("Configuration restored to what it was before the import");
	}
}
