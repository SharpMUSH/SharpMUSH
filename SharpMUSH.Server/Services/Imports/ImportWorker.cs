using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>The private process entry point, selected before normal database/NATS/host initialization.</summary>
public static class ImportWorker
{
	public static async Task<int> RunAsync(string directory)
	{
		var request = JsonSerializer.Deserialize<ImportWorkerRequest>(await File.ReadAllTextAsync(Path.Combine(directory, "request.json")))
			?? throw new IOException("Empty import worker request.");
		var journal = new ImportPromotionJournal(request.LiveWorldPath, request.LiveKeyRingPath);
		var session = journal.Get(Path.GetFileName(Path.GetFullPath(directory)));
		if (session.Directory != Path.GetFullPath(directory) || session.State != ImportSessionState.Preparing)
			throw new IOException("Import worker session is not preparing.");
		using var ownership = new FileStream(Path.Combine(session.Directory, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		using var deadline = new CancellationTokenSource(ImportInputs.Deadline(request.Files));
		try
		{
			foreach (var input in new[] { request.Files.Database, request.Files.Mail, request.Files.Chat, request.Files.MailFolders }.OfType<string>())
				if (Path.GetDirectoryName(Path.GetFullPath(input)) != session.Directory)
					throw new IOException("Import worker inputs must belong to its private session.");
			var source = await new DatabaseImportPreflight(new PennMUSHDatabaseParser(NullLogger<PennMUSHDatabaseParser>.Instance))
				.ReadAsync(request.Files, deadline.Token);
			var configuration = File.Exists(session.ConfigurationPath)
				? ReadPennMushConfig.Import(session.ConfigurationPath, followIncludes: false, source.Dialect switch
				{
					DatabaseDialect.TinyMUX => ConfigurationDialect.TinyMUX,
					DatabaseDialect.RhostMUSH => ConfigurationDialect.RhostMUSH,
					_ => ConfigurationDialect.PennMUSH
				}) : null;
			ConversionResult result;
			var host = await new IsolatedImportHostFactory().CreateAsync(session, deadline.Token);
			try
			{
				var services = host.Services;
				var mediator = services.GetRequiredService<IMediator>();
				var options = services.GetRequiredService<IConfigOptionWriter>();
				var secrets = services.GetRequiredService<ISecretStore>();
				if (request.Baseline is { } baseline)
				{
					await options.UpdateAsync(_ => baseline.Options, deadline.Token);
					await mediator.Send(new SetExpandedServerDataCommand(nameof(MushCnfObjectReferences), baseline.References), deadline.Token);
					await secrets.RestoreAsync(baseline.Secrets, deadline.Token);
				}
				if (configuration is not null)
				{
					await options.UpdateAsync(configuration.Over, deadline.Token);
					await mediator.Send(new SetExpandedServerDataCommand(nameof(MushCnfObjectReferences), MushCnfObjectReferences.From(configuration)), deadline.Token);
					foreach (var (name, value) in configuration.Secrets)
						if (await secrets.SetAsync(name, value, new SecretAuthor("import:" + session.Id, "Database import"), deadline.Token) is Error<string>)
							throw new InvalidOperationException("An imported credential was refused; preview cannot be promoted.");
				}
				journal.Save(session with { State = ImportSessionState.Converting });
				result = await services.GetRequiredService<PennMUSHDatabaseConverter>().ConvertDatabaseAsync(source,
					new WorkerProgress(session.Directory), deadline.Token);
				if (result.Aborted || !result.IsSuccessful || result.TotalObjects == 0)
				{
					journal.Save(session with { State = ImportSessionState.Aborted, Result = result with { Aborted = true }, Failure = "Conversion did not pass validation." });
					return 0;
				}
				await EnsurePackageManagerAsync(mediator, options, result, configuration?.Sets("package_manager") == true, deadline.Token);
				if (configuration is not null) ReportConfiguration(configuration, result);
				journal.Save(session with { State = ImportSessionState.Validating, Result = result });
				if (await mediator.Send(new GetObjectCountQuery(), deadline.Token) < result.TotalObjects)
					throw new IOException("The staged object count is below the conversion's reported count.");
				// Walk the real written objects through this host's mediator, exercising its own node cache.
				var count = 0;
				await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery(), deadline.Token))
				{
					if (obj.DBRef.Number < 0) throw new IOException("The staged world contains an invalid object reference.");
					count++;
				}
				if (count < result.TotalObjects) throw new IOException("The staged object scan is incomplete.");
			}
			finally
			{
				if (host is IAsyncDisposable owned) await owned.DisposeAsync();
				else host.Dispose();
			}
			// Parent owns final readiness after this provider closes AND original sources are revalidated.
			journal.Save(session with { State = ImportSessionState.Validating, Result = result });
			return 0;
		}
		catch (Exception error)
		{
			var diagnostic = Path.Combine(session.Directory, "worker-error.log");
			await File.WriteAllTextAsync(diagnostic, error.GetType().FullName + ": " + error.Message + "\n" + error.StackTrace);
			if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(diagnostic, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			var message = error is OperationCanceledException ? "Import worker was cancelled." : "Import worker failed: " + error.Message;
			journal.Save(session with { State = ImportSessionState.Aborted, Failure = message, Result = new ConversionResult { Aborted = true, Errors = [message] } });
			return 0;
		}
		finally
		{
			// These inputs may contain configuration credentials. Retain the report, not their plaintext.
			foreach (var name in new[] { "request.json", "mush.cnf", "world.source", "mail.source", "chat.source", "folders.source" })
				File.Delete(Path.Combine(session.Directory, name));
		}
	}

	/// <summary>
	/// The configuration lines that were not carried over. An include the upload could not follow is a warning,
	/// since its settings are lost unless they are pasted into the file; the rest, and the directives SharpMUSH has
	/// no option for, are notes.
	/// </summary>
	private static void ReportConfiguration(PennMushConfigImport configuration, ConversionResult result)
	{
		foreach (var line in configuration.Skipped)
			(line.StartsWith("include ", StringComparison.OrdinalIgnoreCase) ? result.Warnings : result.Notes).Add("Configuration: " + line);
		if (configuration.Unrecognized.Count > 0)
			result.Notes.Add($"Configuration: {configuration.Unrecognized.Count} directive(s) SharpMUSH has no option for, not carried over: {string.Join(", ", configuration.Unrecognized)}.");
	}

	/// <param name="fromFile">Whether the uploaded configuration set <c>package_manager</c>, rather than the game keeping its own.</param>
	private static async Task EnsurePackageManagerAsync(IMediator mediator, IConfigOptionWriter options,
		ConversionResult result, bool fromFile, CancellationToken ct)
	{
		var current = await options.CurrentAsync();
		var configured = current.Database.PackageManager;
		if (configured is { } number
			&& await mediator.Send(new GetObjectNodeQuery(new DBRef((int)number)), ct) is AnySharpObject and SharpPlayer)
			return;
		// A mush.cnf written for SharpMUSH (the shipped mushcnf.dst among them) names its own seeded #7, which
		// in the imported database is whatever the source game had there.
		// No bundled packages have run in this private host. The converter therefore had no packages
		// to reinstall, but it still removed the seeded manager to preserve source object numbers.
		var name = "Package Manager";
		for (var suffix = 2; await mediator.CreateStream(new GetPlayerQuery(name), ct).AnyAsync(ct); suffix++)
			name = "Package Manager " + suffix;
		var manager = await mediator.Send(new CreatePlayerCommand(name, string.Empty, new DBRef(0), new DBRef(0),
			999999, Salt: string.Empty, ApplyDefaultFlags: false), ct);
		if (await mediator.Send(new GetObjectFlagQuery("WIZARD"), ct) is not { } wizard
			|| await mediator.Send(new GetObjectNodeQuery(manager), ct) is not AnySharpObject created)
			throw new InvalidOperationException("The staged package manager could not be initialized.");
		await mediator.Send(new SetObjectFlagCommand(created, wizard), ct);
		await options.UpdateAsync(value => value with { Database = value.Database with { PackageManager = (uint)manager.Number } }, ct);
		result.Notes.Add(configured is { } named && named != (uint)manager.Number
			? $"{(fromFile ? "The configuration's" : "This game's")} package_manager is #{named}, which is not a player in the imported database, so a Package Manager '{name}' was created at #{manager.Number}."
			: $"Created a Package Manager '{name}' at #{manager.Number}, past the imported objects, for the bundled packages.");
	}

	private sealed class WorkerProgress(string directory) : IProgress<ConversionProgress>
	{
		public void Report(ConversionProgress value)
			=> File.WriteAllText(Path.Combine(directory, "progress.json"), JsonSerializer.Serialize(value));
	}
}
