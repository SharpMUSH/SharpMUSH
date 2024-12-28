namespace SharpMUSH.Tests.Documentation;

public class HelpfileTests
{
	[Test]
	[Arguments("pennattr.hlp", new[] { "ATTRIBUTE TREES", "ATTR TREES", "ATTRIB TREES", "`" })]
	[Arguments("pennchat.hlp", new[] { "@CEMIT", "@NSCEMIT", "CEMIT()", "NSCEMIT()" })]
	public async Task Indexable(string file, string[] aliasTest)
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var fileString = Path.Combine(currentDirectory, "Documentation", "Testfile", file);
		var fileInfo = new FileInfo(fileString);

		var maybeIndexes = SharpMUSH.Documentation.Helpfiles.Index(fileInfo);

		await Assert.That(maybeIndexes.IsT1).IsNotEqualTo(true);

		var indexes = maybeIndexes.AsT0;

		await Assert.That(indexes).IsNotEmpty();

		foreach (var key in aliasTest)
		{
			await Assert.That(indexes).ContainsKey(key);
		}

		foreach (var key in aliasTest.Skip(1))
		{
			await Assert.That(indexes[key]).IsEqualTo(indexes[aliasTest.First()]);
		}
	}
}