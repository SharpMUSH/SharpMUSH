using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tools.RhostExport;

/// <summary>Runs a trusted upstream exporter against a private copy of an offline backup.</summary>
public sealed class RhostOfflineExporter
{
	private const string Revision = "bfd2894c7239d02b8bec15c36939706be371715c";
	private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
	private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

	public async Task<RhostExportResult> ExportAsync(RhostExportOptions options,
		IProgress<RhostExportProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || !BitConverter.IsLittleEndian)
			throw new PlatformNotSupportedException("The supported exporter profile requires Linux x64 little-endian.");
		cancellationToken.ThrowIfCancellationRequested();
		if (!Enum.IsDefined(options.Backend) || options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > uint.MaxValue - 1
			|| options.MaxSourceBytes <= 0 || options.MaxOutputBytes <= 0 || options.MaxDiagnosticBytes <= 0)
			throw new InvalidDataException("Invalid backend, timeout or byte budget.");
		var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.SourceDirectory));
		var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutputDirectory));
		var parent = Path.GetDirectoryName(output) ?? throw new InvalidDataException("Output must name a new directory.");
		NoLinks(source);
		NoLinks(output);
		if (!Directory.Exists(source)) throw new InvalidDataException("Source directory does not exist.");
		if (Overlaps(source, output)) throw new InvalidDataException("Source and output paths overlap.");
		if (Exists(output)) throw new IOException("Output already exists; no files were replaced.");
		var exporter = Path.GetFullPath(options.ExporterPath);
		var profilePath = Path.GetFullPath(options.ExporterProfilePath);
		NoLinks(exporter);
		NoLinks(profilePath);
		if (Overlaps(output, exporter) || Overlaps(output, profilePath)) throw new InvalidDataException("Output overlaps exporter or profile.");
		var profile = await ValidateProfileAsync(profilePath, exporter, options.Backend, cancellationToken);
		var databaseBase = SourcePath(source, options.DatabaseBase);
		var checkpoint = SourcePath(source, options.Checkpoint);
		if (databaseBase == checkpoint) throw new InvalidDataException("Checkpoint and backing-store base must differ.");
		var suffixes = options.Backend == RhostBackend.LegacyQdbm ? new[] { ".db", ".dir", ".pag" } : [".mdbx"];
		var incompatible = options.Backend == RhostBackend.LegacyQdbm ? new[] { ".mdbx", ".mdbx-lck" } : [".db", ".dir", ".pag"];
		if (incompatible.Any(suffix => Exists(databaseBase + suffix))) throw new InvalidDataException("Mixed or contradictory backend companions.");
		var inputs = new List<(string Source, string PrivateName)> { (checkpoint, "checkpoint") };
		inputs.AddRange(suffixes.Select(suffix => (databaseBase + suffix, "worldstore" + suffix)));
		var mail = options.MailFile is null ? null : SourcePath(source, options.MailFile);
		var folders = options.FolderFile is null ? null : SourcePath(source, options.FolderFile);
		if (folders is not null && mail is null) throw new InvalidDataException("Folder text requires a mail text companion.");
		var mailBases = new[] { databaseBase, databaseBase.EndsWith(".gdbm", StringComparison.Ordinal) ? databaseBase[..^5] : databaseBase }.Distinct().ToArray();
		var mailSuffixes = options.Backend == RhostBackend.LegacyQdbm ? new[] { ".mail.dir", ".mail.pag" } : [".mail", ".mail-lck"];
		var folderSuffixes = options.Backend == RhostBackend.LegacyQdbm ? new[] { ".folder.dir", ".folder.pag" } : [".folder", ".folder-lck"];
		if (mailBases.Any(name => mailSuffixes.Any(suffix => Exists(name + suffix))) && mail is null)
			throw new InvalidDataException("Native mail exists; supply a portable wmail/unload mail companion.");
		if (mailBases.Any(name => folderSuffixes.Any(suffix => Exists(name + suffix))) && folders is null)
			throw new InvalidDataException("Native folders exist; supply a portable wmail/unload folder companion.");
		if (mail is not null) inputs.Add((mail, "mail.dump"));
		if (folders is not null) inputs.Add((folders, "folders.dump"));
		if (inputs.Select(x => x.Source).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
			throw new InvalidDataException("Source roles must name distinct files.");
		progress?.Report(new("Inventorying source"));
		var inventory = new List<RhostSourceFile>();
		long total = 0;
		foreach (var input in inputs)
		{
			NoLinks(input.Source);
			var info = new FileInfo(input.Source);
			if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) throw new InvalidDataException("Missing required source file: " + input.Source);
			if (info.Length > options.MaxSourceBytes - total) throw new InvalidDataException("Source exceeds byte budget.");
			total += info.Length;
			inventory.Add(await InventoryAsync(input.Source, source, cancellationToken));
		}
		await ValidateCheckpointAsync(checkpoint, cancellationToken);
		Directory.CreateDirectory(parent);
		NoLinks(parent);
		var scratch = Path.Combine(parent, ".rhost-export-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(scratch, DirectoryMode);
		var privateCopy = Path.Combine(scratch, "source");
		var incoming = Path.Combine(scratch, "incoming");
		Directory.CreateDirectory(privateCopy, DirectoryMode);
		Directory.CreateDirectory(incoming, DirectoryMode);
		try
		{
			for (var i = 0; i < inputs.Count; i++)
			{
				var destination = Path.Combine(privateCopy, inputs[i].PrivateName);
				await CopyLimitedAsync(inputs[i].Source, destination, options.MaxSourceBytes, cancellationToken);
				if (await HashAsync(destination, cancellationToken) != inventory[i].Sha256) throw new InvalidDataException("Source changed while making private copy.");
			}
			var flatPath = Path.Combine(incoming, "world.flat");
			progress?.Report(new("Exporting private copy"));
			var diagnostics = await RunExporterAsync(exporter, privateCopy, flatPath, options, cancellationToken);
			ValidateDiagnostics(diagnostics, Path.Combine(privateCopy, "worldstore"), options.Backend);
			progress?.Report(new("Validating portable export"));
			RhostFlatfile flat;
			RhostMail? parsedMail = null;
			try
			{
				flat = await RhostFlatfileReader.ReadFileStrictAsync(flatPath, cancellationToken);
				if (mail is not null)
				{
					var privateMail = Path.Combine(privateCopy, "mail.dump");
					var privateFolders = folders is null ? null : Path.Combine(privateCopy, "folders.dump");
					parsedMail = await RhostMailReader.ReadFilesStrictAsync(privateMail, privateFolders, cancellationToken);
					await CopyLimitedAsync(privateMail, Path.Combine(incoming, "mail.dump"), options.MaxSourceBytes, cancellationToken);
					if (privateFolders is not null) await CopyLimitedAsync(privateFolders, Path.Combine(incoming, "folders.dump"), options.MaxSourceBytes, cancellationToken);
				}
			}
			catch (FormatException ex) { throw new InvalidDataException("Malformed portable export: " + ex.Message, ex); }
			var warnings = RhostDatabaseTranslator.Translate(flat, parsedMail).ImportNotes;
			warnings.Add("Export validation cannot independently prove every external attribute was present or readable; retain the coherent offline source backup.");
			if (mail is null) warnings.Add("Portable mail was not supplied; dbconvert does not export native mail stores.");
			for (var i = 0; i < inputs.Count; i++)
			{
				NoLinks(inputs[i].Source);
				if (await InventoryAsync(inputs[i].Source, source, cancellationToken) != inventory[i]) throw new InvalidDataException("Original source changed during export.");
			}
			var result = new RhostExportResult(output, flat.Objects.Count, flat.Objects.Sum(x => x.Attributes.Count), warnings, inventory);
			var outputInventory = new List<RhostSourceFile>();
			foreach (var file in Directory.GetFiles(incoming).Order(StringComparer.Ordinal)) outputInventory.Add(await InventoryAsync(file, incoming, cancellationToken));
			await using (var manifest = CreatePrivateFile(Path.Combine(incoming, "manifest.json")))
				await JsonSerializer.SerializeAsync(manifest, new { schema = 1, upstreamRevision = Revision, backend = BackendName(options.Backend), profile, result.ObjectCount, result.AttributeCount, sourceInventory = inventory, outputInventory, warnings }, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			NoLinks(output);
			Directory.Move(incoming, output); // The sole publication point; never overwrites an existing directory.
			progress?.Report(new("Published"));
			return result;
		}
		finally { Directory.Delete(scratch, true); }
	}

	private static async Task<JsonElement> ValidateProfileAsync(string path, string exporter, RhostBackend backend, CancellationToken ct)
	{
		if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("Exporter profile exceeds byte budget.");
		try
		{
			using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, ct));
			var p = document.RootElement;
			if (p.ValueKind != JsonValueKind.Object || p.EnumerateObject().Select(x => x.Name).Distinct().Count() != p.EnumerateObject().Count()
				|| p.GetProperty("schema").GetInt32() != 1 || p.GetProperty("upstreamRevision").GetString() != Revision
				|| p.GetProperty("backend").GetString() != BackendName(backend) || p.GetProperty("rid").GetString() != "linux-x64"
				|| !p.GetProperty("littleEndian").GetBoolean() || p.GetProperty("pointerSize").GetInt32() != 8
				|| p.GetProperty("intSize").GetInt32() != 4 || p.GetProperty("offTSize").GetInt32() != 8
				|| !string.Equals(p.GetProperty("exporterSha256").GetString(), await HashAsync(exporter, ct), StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Exporter profile, ABI, backend or SHA256 does not match the supported build.");
			return p.Clone();
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{ throw new InvalidDataException("Invalid exporter profile.", ex); }
	}

	private static async Task ValidateCheckpointAsync(string path, CancellationToken ct)
	{
		await using var file = File.OpenRead(path);
		var bytes = new byte[64];
		var length = await file.ReadAsync(bytes, ct);
		var lineEnd = Array.IndexOf(bytes, (byte)'\n', 0, length);
		if (lineEnd <= 2 || bytes[0] != '+' || bytes[1] != 'V'
			|| !int.TryParse(Encoding.ASCII.GetString(bytes, 2, lineEnd - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
			|| (version & 0xff) != 7 || (version & 0x400) == 0)
			throw new InvalidDataException("Checkpoint must start with a native v7 +V header with external attributes.");
	}

	private static string SourcePath(string source, string name)
	{
		var path = Path.GetFullPath(Path.Combine(source, name));
		if (Path.GetDirectoryName(path) != source || string.IsNullOrEmpty(Path.GetFileName(path)))
			throw new InvalidDataException("Source files and database base must be direct children of the source directory.");
		return path;
	}

	private static string BackendName(RhostBackend backend) => backend == RhostBackend.LegacyQdbm ? "legacy-qdbm" : "mdbx";
	private static bool Overlaps(string a, string b) => a == b || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.Ordinal) || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	private static bool Exists(string path)
	{
		try { _ = File.GetAttributes(path); return true; }
		catch (FileNotFoundException) { return false; }
		catch (DirectoryNotFoundException) { return false; }
	}
	private static void NoLinks(string path)
	{
		for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
			if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException("Symbolic links/reparse paths are not supported: " + current);
	}
	private static async Task<string> HashAsync(string path, CancellationToken ct)
	{
		await using var stream = File.OpenRead(path);
		return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
	}
	private static async Task<RhostSourceFile> InventoryAsync(string path, string root, CancellationToken ct)
		=> new(Path.GetRelativePath(root, path), new FileInfo(path).Length, await HashAsync(path, ct));
	private static FileStream CreatePrivateFile(string path)
	{
		var options = new FileStreamOptions
		{
			Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
			Options = FileOptions.Asynchronous
		};
		if (OperatingSystem.IsLinux()) options.UnixCreateMode = FileMode;
		return new FileStream(path, options);
	}

	private static async Task CopyLimitedAsync(string source, string destination, long limit, CancellationToken ct)
	{
		await using var input = File.OpenRead(source);
		await using var output = CreatePrivateFile(destination);
		await PumpAsync(input, output, limit, ct);
	}
	private static async Task PumpAsync(Stream input, Stream output, long limit, CancellationToken ct)
	{
		var buffer = new byte[16 * 1024];
		long total = 0;
		int count;
		while ((count = await input.ReadAsync(buffer, ct)) != 0)
		{
			if (count > limit - total) throw new InvalidDataException("Exporter stream exceeds byte budget.");
			total += count;
			await output.WriteAsync(buffer.AsMemory(0, count), ct);
		}
	}

	private static async Task<string> RunExporterAsync(string executable, string privateCopy, string outputPath, RhostExportOptions options, CancellationToken ct)
	{
		var start = new ProcessStartInfo(executable)
		{
			UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
			RedirectStandardError = true, WorkingDirectory = privateCopy
		};
		start.ArgumentList.Add(Path.Combine(privateCopy, "worldstore"));
		start.ArgumentList.Add("x");
		foreach (var variable in new[] { "TMPDIR", "TMP", "TEMP" }) start.Environment[variable] = privateCopy;
		using var process = new Process { StartInfo = start };
		await using var input = File.OpenRead(Path.Combine(privateCopy, "checkpoint"));
		await using var output = CreatePrivateFile(outputPath);
		using var error = new MemoryStream();
		using var timeout = new CancellationTokenSource(options.Timeout);
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
		Exception? firstError = null;
		process.Start();
		using var registration = stop.Token.Register(() =>
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
			catch (InvalidOperationException) { }
		});
		async Task GuardAsync(Func<Task> action)
		{
			try { await action(); }
			catch (Exception ex)
			{
				Interlocked.CompareExchange(ref firstError, ex, null);
				stop.Cancel();
				throw;
			}
		}
		var inputPump = GuardAsync(async () =>
		{
			try { await PumpAsync(input, process.StandardInput.BaseStream, options.MaxSourceBytes, stop.Token); }
			finally { process.StandardInput.Close(); }
		});
		var outputPump = GuardAsync(() => PumpAsync(process.StandardOutput.BaseStream, output, options.MaxOutputBytes, stop.Token));
		var errorPump = GuardAsync(() => PumpAsync(process.StandardError.BaseStream, error, options.MaxDiagnosticBytes, stop.Token));
		var wait = GuardAsync(() => process.WaitForExitAsync(stop.Token));
		try { await Task.WhenAll(inputPump, outputPump, errorPump, wait); }
		catch
		{
			stop.Cancel();
			await process.WaitForExitAsync(CancellationToken.None);
			ct.ThrowIfCancellationRequested();
			if (timeout.IsCancellationRequested) throw new TimeoutException("Exporter exceeded its timeout.");
			if (firstError is not null) ExceptionDispatchInfo.Capture(firstError).Throw();
			throw;
		}
		ct.ThrowIfCancellationRequested();
		if (timeout.IsCancellationRequested) throw new TimeoutException("Exporter exceeded its timeout.");
		if (process.ExitCode != 0) throw new InvalidDataException($"Exporter exited with code {process.ExitCode}.");
		return Encoding.UTF8.GetString(error.ToArray());
	}

	private static void ValidateDiagnostics(string diagnostics, string privateBase, RhostBackend backend)
	{
		if (diagnostics.Length == 0) return;
		var path = Regex.Escape(privateBase);
		var mdbx = backend == RhostBackend.Mdbx
			? @"(?:dxb_setup:[0-9]{1,8} open-MADV_DONTNEED [0-9]{1,20}\.\.[0-9]{1,20}\n)?(?:dxb_set_readahead:[0-9]{1,8} readahead (?:ON|OFF) [0-9]{1,20}\.\.[0-9]{1,20}\n)?" : "";
		var pattern = @"\AOpening " + path + "\n" + mdbx + "Done opening " + path + @"\.\nReading \.\nInput: TinyMUSH version 7: Link ExtAttrs AtrName AtrKey Parent AtrMoney ExtFlags\nOutput: TinyMUSH version 7: Link Parent ExtFlags\nWriting \.\n\z";
		if (!Regex.IsMatch(diagnostics, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
			throw new InvalidDataException("Exporter emitted unrecognized diagnostics: " + diagnostics);
	}
}
