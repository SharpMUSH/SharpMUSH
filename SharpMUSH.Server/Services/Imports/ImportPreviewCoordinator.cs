using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;
using SharpMUSH.Library.Plugins;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services.Imports;

public sealed record ImportWorkerRequest(string LiveWorldPath, string LiveKeyRingPath, DatabaseImportFiles Files,
	ConfigurationSnapshot? Baseline);

/// <summary>Owns input snapshots and a bounded child process; never resolves a live converter or forwards a live store.</summary>
public sealed class ImportPreviewCoordinator(ImportPromotionJournal journal, ILogger<ImportPreviewCoordinator> logger)
{
	public Task<ImportSession> PreviewAsync(DatabaseImportFiles files, string? configuration = null,
		ConfigurationSnapshot? baseline = null, CancellationToken cancellationToken = default)
		=> PreviewSessionAsync(journal.Create(), files, configuration, baseline, cancellationToken);

	internal async Task<ImportSession> PreviewSessionAsync(ImportSession session, DatabaseImportFiles files, string? configuration,
		ConfigurationSnapshot? baseline, CancellationToken cancellationToken)
	{
		try
		{
			using var ownership = new FileStream(Path.Combine(journal.SessionsPath, "preview.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			cancellationToken.ThrowIfCancellationRequested();
			var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			async Task<string?> CopyAsync(string? source, string name)
			{
				if (source is null) return null;
				if (!File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
					throw new IOException("A declared source is missing or is a symbolic link.");
				if (new FileInfo(source).Length > 512L << 20) throw new IOException("An import input exceeds the 512 MiB worker limit.");
				var destination = Path.Combine(session.Directory, name);
				var before = await HashAsync(source, cancellationToken);
				await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
				await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				await input.CopyToAsync(output, cancellationToken);
				await output.FlushAsync(cancellationToken);
				await output.DisposeAsync();
				var after = await HashAsync(source, cancellationToken);
				if (!before.AsSpan().SequenceEqual(after) || !(await HashAsync(destination, cancellationToken)).AsSpan().SequenceEqual(after))
					throw new IOException("An import source changed while it was copied.");
				sources[source] = after;
				return destination;
			}
			var copied = new DatabaseImportFiles((await CopyAsync(files.Database, "world.source"))!,
				await CopyAsync(files.Mail, "mail.source"), await CopyAsync(files.Chat, "chat.source"),
				await CopyAsync(files.MailFolders, "folders.source"), files.Format);
			if (configuration is not null)
			{
				if (configuration.Length > 1024 * 1024) throw new IOException("Import configuration exceeds 1 MiB.");
				await File.WriteAllTextAsync(session.ConfigurationPath, configuration, cancellationToken);
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(session.ConfigurationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			ImportPromotionJournal.PrivateDirectory(session.KeyRingPath);
			if (baseline is not null && Directory.Exists(journal.LiveKeyRingPath))
				foreach (var key in Directory.EnumerateFiles(journal.LiveKeyRingPath, "*.xml"))
				{
					if ((File.GetAttributes(key) & FileAttributes.ReparsePoint) != 0) throw new IOException("The source key ring contains a symbolic link.");
					var copy = Path.Combine(session.KeyRingPath, Path.GetFileName(key));
					File.Copy(key, copy);
					if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				}
			// Catalog registration can inspect installed code/state. Give it a private snapshot too.
			var installed = PluginDirectories.ForWorld(journal.LiveWorldPath,
				Environment.GetEnvironmentVariable(PluginDirectories.PathVariable)).Installed;
			var privatePlugins = Path.Combine(session.Directory, "plugins");
			ImportPromotionJournal.PrivateDirectory(privatePlugins);
			if (Directory.Exists(installed))
			{
				var pending = new Stack<string>();
				pending.Push(installed);
				while (pending.TryPop(out var directory))
					foreach (var source in Directory.EnumerateFileSystemEntries(directory))
					{
						var attributes = File.GetAttributes(source);
						if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Plugin snapshots cannot contain symbolic links.");
						var relative = Path.GetRelativePath(installed, source);
						if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is ".uploads" or ".ui")) continue;
						var destination = Path.Combine(privatePlugins, relative);
						if ((attributes & FileAttributes.Directory) != 0)
						{
							ImportPromotionJournal.PrivateDirectory(destination);
							pending.Push(source);
						}
						else File.Copy(source, destination);
					}
			}

			var request = new ImportWorkerRequest(journal.LiveWorldPath, journal.LiveKeyRingPath, copied, baseline);
			await File.WriteAllTextAsync(Path.Combine(session.Directory, "request.json"), JsonSerializer.Serialize(request), cancellationToken);
			if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(session.Directory, "request.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromMinutes(10));
			await RunWorkerAsync(session, deadline.Token);
			session = journal.Get(session.Id);
			foreach (var (source, hash) in sources)
				if (!(await HashAsync(source, cancellationToken)).AsSpan().SequenceEqual(hash))
					throw new IOException("A source input changed during preview; promotion is refused.");
			if (session.State == ImportSessionState.Aborted) CleanupStaging(session);
			return session;
		}
		catch (Exception error)
		{
			logger.LogWarning("Import session {SessionId} aborted: {ErrorType}", session.Id, error.GetType().Name);
			var failure = error is OperationCanceledException ? "Import preview was cancelled." : "Import preview failed: " + error.GetType().Name;
			session = journal.Get(session.Id) with
			{
				State = ImportSessionState.Aborted,
				Failure = failure,
				Result = new ConversionResult { Aborted = true, Errors = [failure] }
			};
			journal.Save(session);
			CleanupStaging(session);
			return session;
		}
		finally
		{
			foreach (var name in new[] { "request.json", "mush.cnf", "world.source", "mail.source", "chat.source", "folders.source" })
				File.Delete(Path.Combine(session.Directory, name));
		}
	}

	private static async Task<byte[]> HashAsync(string path, CancellationToken ct)
	{
		await using var stream = File.OpenRead(path);
		return await SHA256.HashDataAsync(stream, ct);
	}

	private static async Task RunWorkerAsync(ImportSession session, CancellationToken ct)
	{
		var assembly = typeof(Program).Assembly.Location;
		var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
		{
			UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
			WorkingDirectory = session.Directory
		};
		// Use the running host's dependency closure, including hosts that embed Server for tests/tools.
		var entry = Assembly.GetEntryAssembly()?.Location ?? assembly;
		start.ArgumentList.Add("exec");
		start.ArgumentList.Add("--runtimeconfig");
		start.ArgumentList.Add(Path.ChangeExtension(entry, ".runtimeconfig.json"));
		start.ArgumentList.Add("--depsfile");
		start.ArgumentList.Add(Path.ChangeExtension(entry, ".deps.json"));
		start.ArgumentList.Add(assembly);
		start.ArgumentList.Add("--import-worker");
		start.ArgumentList.Add(session.Directory);
		using var process = Process.Start(start) ?? throw new IOException("Import worker could not start.");
		async Task DrainAsync(Stream input)
		{
			var buffer = new byte[8192];
			long total = 0;
			int read;
			try
			{
				while ((read = await input.ReadAsync(buffer, ct)) > 0)
				{
					total += read;
					if (total > 16L << 20) throw new IOException("Import worker diagnostic output limit exceeded.");
				}
			}
			catch
			{
				if (!process.HasExited) process.Kill(entireProcessTree: true);
				throw;
			}
		}
		var stdout = DrainAsync(process.StandardOutput.BaseStream);
		var stderr = DrainAsync(process.StandardError.BaseStream);
		try
		{
			await Task.WhenAll(process.WaitForExitAsync(ct), stdout, stderr).WaitAsync(ct);
			if (process.ExitCode != 0) throw new IOException("Import worker exited unsuccessfully.");
		}
		catch
		{
			if (!process.HasExited) process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync(CancellationToken.None);
			try { await Task.WhenAll(stdout, stderr); } catch (Exception) { /* The original worker failure owns this result. */ }
			throw;
		}
	}

	internal static void CleanupStaging(ImportSession session)
	{
		foreach (var directory in new[] { session.WorldPath, session.KeyRingPath, Path.Combine(session.Directory, "plugins") })
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
	}
}
