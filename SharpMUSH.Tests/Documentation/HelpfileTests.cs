namespace SharpMUSH.Tests.Documentation;

public class HelpfileTests
{
	[Test]
	public async Task Indexable()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var fileString = Path.Combine(currentDirectory, "Documentation", "Testfile", "pennattr.hlp");
		var fileInfo = new FileInfo(fileString);
		Console.WriteLine($"Helpfile Tests registering directory: {fileInfo.DirectoryName}");
		var maybeIndexes = SharpMUSH.Documentation.Helpfiles.Index(fileInfo);

		await Assert.That(maybeIndexes.IsT1).IsNotEqualTo(true);

		var indexes = maybeIndexes.AsT0;

		await Assert.That(indexes).IsNotEmpty();

		foreach (var key in new[] { "ATTRIBUTE TREES", "ATTR TREES", "ATTRIB TREES", "`" })
		{
			await Assert.That(indexes).ContainsKey(key);
		}
		
		foreach (var key in new[] { "ATTRIBUTE TREES", "ATTR TREES", "ATTRIB TREES" })
		{
			await Assert.That(indexes[key]).IsEqualTo(indexes["`"]);
		}
	}
}