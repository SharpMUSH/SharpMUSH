using SharpMUSH.Tools.ClientData;

if (args.Length != 2)
{
	Console.Error.WriteLine("usage: ClientData <helpfile directory> <output directory>");
	return 2;
}

var output = Directory.CreateDirectory(args[1]);
foreach (var (name, contents) in ClientDataGenerator.Files(new DirectoryInfo(args[0])))
	await File.WriteAllTextAsync(Path.Join(output.FullName, name), contents);

return 0;
