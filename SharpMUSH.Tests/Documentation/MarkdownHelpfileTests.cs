using SharpMUSH.Documentation;

namespace SharpMUSH.Tests.Documentation;

public class MarkdownHelpfileTests
{
	[Test]
	public async Task CanIndexMarkdownHeaders()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelp.md");

		// Create a test markdown file
		var testContent = @"# help
This is the main help page.

# test topic
This is a test topic with some content.

# another topic
This is another topic.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;

			await Assert.That(indexes.Count).IsEqualTo(3);
			await Assert.That(indexes).ContainsKey("help");
			await Assert.That(indexes).ContainsKey("test topic");
			await Assert.That(indexes).ContainsKey("another topic");
		}
		finally
		{
			if (File.Exists(testFilePath))
			{
				File.Delete(testFilePath);
			}
		}
	}

	[Test]
	public async Task IndexedMarkdownIncludesHeaderAndContent()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelp2.md");

		var testContent = @"# commands
Help is available for the following commands:

- look
- examine
- help

# look
The look command allows you to see your surroundings.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;
			var lookEntry = indexes["look"];

			await Assert.That(lookEntry).Contains("# look");
			await Assert.That(lookEntry).Contains("The look command allows you to see your surroundings.");
		}
		finally
		{
			if (File.Exists(testFilePath))
			{
				File.Delete(testFilePath);
			}
		}
	}

	[Test]
	public async Task FindEntryCaseInsensitive()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testDir = Path.Combine(currentDirectory, "HelpTest");
		Directory.CreateDirectory(testDir);

		try
		{
			var testFilePath = Path.Combine(testDir, "test.md");
			var testContent = @"# Help
This is help content.

# COMMANDS
This is commands content.
";
			await File.WriteAllTextAsync(testFilePath, testContent);

			var helpfiles = new Helpfiles(new DirectoryInfo(testDir));
			helpfiles.Index();

			// Test case-insensitive lookup
			var helpEntry = helpfiles.FindEntry("help");
			await Assert.That(helpEntry).IsNotNull();
			await Assert.That(helpEntry).Contains("This is help content");

			var commandsEntry = helpfiles.FindEntry("commands");
			await Assert.That(commandsEntry).IsNotNull();
			await Assert.That(commandsEntry).Contains("This is commands content");
		}
		finally
		{
			if (Directory.Exists(testDir))
			{
				Directory.Delete(testDir, true);
			}
		}
	}

	[Test]
	public async Task FindMatchingTopicsWithWildcard()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testDir = Path.Combine(currentDirectory, "HelpTest2");
		Directory.CreateDirectory(testDir);

		try
		{
			var testFilePath = Path.Combine(testDir, "test.md");
			var testContent = @"# @create
Create command

# @destroy
Destroy command

# @dig
Dig command

# look
Look command
";
			await File.WriteAllTextAsync(testFilePath, testContent);

			var helpfiles = new Helpfiles(new DirectoryInfo(testDir));
			helpfiles.Index();

			// Test wildcard matching
			var matches = helpfiles.FindMatchingTopics("@*").ToList();
			await Assert.That(matches.Count).IsEqualTo(3);
			await Assert.That(matches).Contains("@create");
			await Assert.That(matches).Contains("@destroy");
			await Assert.That(matches).Contains("@dig");
		}
		finally
		{
			if (Directory.Exists(testDir))
			{
				Directory.Delete(testDir, true);
			}
		}
	}

	[Test]
	public async Task SearchContentFindsMatches()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testDir = Path.Combine(currentDirectory, "HelpTest3");
		Directory.CreateDirectory(testDir);

		try
		{
			var testFilePath = Path.Combine(testDir, "test.md");
			var testContent = @"# @create
The @create command creates a new object.

# @destroy
The @destroy command destroys an object.

# look
The look command lets you examine your surroundings.
";
			await File.WriteAllTextAsync(testFilePath, testContent);

			var helpfiles = new Helpfiles(new DirectoryInfo(testDir));
			helpfiles.Index();

			// Test content search
			var matches = helpfiles.SearchContent("object").ToList();
			await Assert.That(matches.Count).IsEqualTo(2);
			await Assert.That(matches).Contains("@create");
			await Assert.That(matches).Contains("@destroy");
		}
		finally
		{
			if (Directory.Exists(testDir))
			{
				Directory.Delete(testDir, true);
			}
		}
	}
}
