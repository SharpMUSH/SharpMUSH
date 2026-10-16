using SharpMUSH.Tools.RhostExport;

if (args.Length == 0 || args.Contains("--help"))
{
	Console.WriteLine("RhostExport --source DIR --base NAME --checkpoint FILE --backend legacy-qdbm|mdbx --exporter FILE --profile FILE --output NEWDIR [--mail FILE] [--folders FILE]");
	return 0;
}
try
{
	if (args.Length % 2 != 0) throw new ArgumentException("Every option requires a value.");
	var values = new Dictionary<string, string>(StringComparer.Ordinal);
	var allowed = new[] { "--source", "--base", "--checkpoint", "--backend", "--exporter", "--profile", "--output", "--mail", "--folders" };
	for (var i = 0; i < args.Length; i += 2)
		if (!allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate option: " + args[i]);
	using var cancellation = new CancellationTokenSource();
	Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
	var result = await new RhostOfflineExporter().ExportAsync(new()
	{
		SourceDirectory = values["--source"], DatabaseBase = values["--base"], Checkpoint = values["--checkpoint"],
		Backend = values["--backend"] switch { "legacy-qdbm" => RhostBackend.LegacyQdbm, "mdbx" => RhostBackend.Mdbx, _ => throw new ArgumentException("Unsupported backend.") },
		ExporterPath = values["--exporter"], ExporterProfilePath = values["--profile"], OutputDirectory = values["--output"],
		MailFile = values.GetValueOrDefault("--mail"), FolderFile = values.GetValueOrDefault("--folders")
	}, cancellationToken: cancellation.Token);
	Console.WriteLine($"Exported {result.ObjectCount} objects and {result.AttributeCount} attributes to {result.OutputDirectory}");
	foreach (var warning in result.Warnings) Console.Error.WriteLine(warning);
	return 0;
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
	Console.Error.WriteLine(ex.Message);
	return 1;
}
