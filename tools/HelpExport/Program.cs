using SharpMUSH.Documentation;

if (args.Length != 2)
{
	Console.Error.WriteLine("usage: HelpExport <repository root> <output directory>");
	return 2;
}

try
{
	var repository = new DirectoryInfo(args[0]);
	var plugins = HelpExport.ShippedPlugins(repository);
	var files = HelpExport.Build(HelpExport.EngineHelp(repository), plugins);
	HelpExport.Write(files, new DirectoryInfo(args[1]));
	Console.WriteLine($"Exported {files.Count} help files, {files.Count(file => file.Plugin is not null)} of them from "
		+ $"{plugins.Count} plugins, to {Path.GetFullPath(args[1])}");
	return 0;
}
catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
{
	Console.Error.WriteLine(error.Message);
	return 1;
}
