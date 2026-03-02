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
	public async Task ConsecutiveHeadersAreAliasesWithSharedContent()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpAliases.md");

		// Consecutive headers with no content between them are aliases for the same body.
		var testContent = @"# look
# read
`look [<object>]`

Displays the description of an object.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;

			// Both 'look' and 'read' should be present.
			await Assert.That(indexes.Count).IsEqualTo(2);
			await Assert.That(indexes).ContainsKey("look");
			await Assert.That(indexes).ContainsKey("read");

			// Both entries should include the shared content, not just the header.
			await Assert.That(indexes["look"]).Contains("Displays the description of an object.");
			await Assert.That(indexes["read"]).Contains("Displays the description of an object.");
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

	[Test]
	public async Task ConsecutiveAliasHeadersHaveContent()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpConsecutiveAliases.md");

		// Consecutive alias headers (no blank lines between them) share the same content block.
		// This mirrors the "Getting Started"/"GS"/"Walkthrough" pattern in sharptop.md.
		var testContent = @"# Getting Started
# GS
# Walkthrough
  This helpfile is a quick walkthrough of some of SharpMUSH's standard systems.

  For help with getting around, please see [gs moving].

# GS MOVING
  To see the room you're in, type 'look'.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;

			// All three aliases should be present.
			await Assert.That(indexes).ContainsKey("Getting Started");
			await Assert.That(indexes).ContainsKey("GS");
			await Assert.That(indexes).ContainsKey("Walkthrough");
			await Assert.That(indexes).ContainsKey("GS MOVING");

			// All three aliases should include the shared body content, not just the header.
			await Assert.That(indexes["Getting Started"]).Contains("quick walkthrough");
			await Assert.That(indexes["GS"]).Contains("quick walkthrough");
			await Assert.That(indexes["Walkthrough"]).Contains("quick walkthrough");

			// GS MOVING should have its own content.
			await Assert.That(indexes["GS MOVING"]).Contains("type 'look'");
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
	public async Task ConsecutiveAliasesFollowedByContentHaveContent()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpWhoPattern.md");

		// Simulates the "WHO"/"DOING" pattern from sharpcmd.md.
		var testContent = @"# whisper
The whisper command lets you whisper to someone.

# WHO
# DOING
`WHO [<pattern>]`
`DOING [<pattern>]`

For mortals, the WHO command displays a list of players currently connected.

See [who2].
# WHO2
Existing games which have softcoded 'who' commands.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;

			await Assert.That(indexes).ContainsKey("WHO");
			await Assert.That(indexes).ContainsKey("DOING");
			await Assert.That(indexes).ContainsKey("WHO2");

			// Both WHO and DOING should include the full shared content.
			await Assert.That(indexes["WHO"]).Contains("displays a list of players currently connected");
			await Assert.That(indexes["DOING"]).Contains("displays a list of players currently connected");

			// WHO2 should have its own content.
			await Assert.That(indexes["WHO2"]).Contains("softcoded");
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
	public async Task FunctionListAliasHasContent()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpFuncList.md");

		// Simulates the "FUNCTION LIST"/"FUNCTION TYPES" pattern from sharpfunc.md.
		var testContent = @"# FUNCTIONS2
  There are two types of functions.

# FUNCTION LIST
# FUNCTION TYPES
  Several major variants of functions are available.

  - Attribute functions
  - Bitwise functions

# Attribute functions
  These functions can access or alter information stored in attributes.
";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;

			await Assert.That(indexes).ContainsKey("FUNCTION LIST");
			await Assert.That(indexes).ContainsKey("FUNCTION TYPES");
			await Assert.That(indexes).ContainsKey("Attribute functions");

			// Both FUNCTION LIST and FUNCTION TYPES should include the shared content.
			await Assert.That(indexes["FUNCTION LIST"]).Contains("Several major variants");
			await Assert.That(indexes["FUNCTION TYPES"]).Contains("Several major variants");

			// Attribute functions should have its own content.
			await Assert.That(indexes["Attribute functions"]).Contains("stored in attributes");
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
	public async Task RealHelpFilesHaveContentForKnownAliases()
	{
		// Test the actual help files in the repository to ensure the reported
		// bug (entries showing only headers) is fixed.
		var helpDir = FindHelpfilesDirectory();
		if (helpDir == null)
		{
			// Skip if help files directory not found (e.g. running outside repo).
			return;
		}

		var helpfiles = new Helpfiles(new DirectoryInfo(helpDir));
		helpfiles.Index();

		// These entries were reported as showing only headers with no content.
		var functionList = helpfiles.FindEntry("FUNCTION LIST");
		await Assert.That(functionList).IsNotNull();
		await Assert.That(functionList!).Contains("Several major variants");

		var gettingStarted = helpfiles.FindEntry("Getting Started");
		await Assert.That(gettingStarted).IsNotNull();
		await Assert.That(gettingStarted!).Contains("walkthrough");

		var who = helpfiles.FindEntry("WHO");
		await Assert.That(who).IsNotNull();
		await Assert.That(who!).Contains("WHO command");
	}

	[Test]
	public async Task PositionBasedIndexingReturnsCorrectContentForAliases()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpPositions.md");

		// Consecutive alias headers share the same byte range.
		var testContent = "# WHO\n# DOING\n`WHO [<pattern>]`\n\nFor mortals, the WHO command displays players.\n\n# WHO2\nSoftcoded who commands.\n";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybePositions = Helpfiles.IndexMarkdownPositions(fileInfo);

			await Assert.That(maybePositions.IsT0).IsTrue();

			var positions = maybePositions.AsT0;

			await Assert.That(positions).ContainsKey("WHO");
			await Assert.That(positions).ContainsKey("DOING");
			await Assert.That(positions).ContainsKey("WHO2");

			// WHO and DOING should share the same byte range (aliases).
			await Assert.That(positions["WHO"].Start).IsEqualTo(positions["DOING"].Start);
			await Assert.That(positions["WHO"].End).IsEqualTo(positions["DOING"].End);

			// WHO2 should have a different range.
			await Assert.That(positions["WHO2"].Start).IsNotEqualTo(positions["WHO"].Start);

			// Verify the byte ranges produce correct content when read from file.
			var fileBytes = await File.ReadAllBytesAsync(testFilePath);

			var whoStart = (int)positions["WHO"].Start;
			var whoEnd = (int)positions["WHO"].End;
			var whoContent = System.Text.Encoding.UTF8.GetString(fileBytes, whoStart, whoEnd - whoStart);
			await Assert.That(whoContent).Contains("# WHO");
			await Assert.That(whoContent).Contains("displays players");

			var who2Start = (int)positions["WHO2"].Start;
			var who2End = (int)positions["WHO2"].End;
			var who2Content = System.Text.Encoding.UTF8.GetString(fileBytes, who2Start, who2End - who2Start);
			await Assert.That(who2Content).Contains("# WHO2");
			await Assert.That(who2Content).Contains("Softcoded");
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
	public async Task PositionBasedIndexingHandlesCrlfLineEndings()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpCrlf.md");

		// Write a file with explicit CRLF (\r\n) line endings to simulate Windows checkouts.
		var testContent = "# help\r\nThis is the main help.\r\n\r\n# newbie\r\nNewbie help content here.\r\n\r\n# newbie2\r\nMore newbie content.\r\n";
		await File.WriteAllBytesAsync(testFilePath, System.Text.Encoding.UTF8.GetBytes(testContent));

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybePositions = Helpfiles.IndexMarkdownPositions(fileInfo);

			await Assert.That(maybePositions.IsT0).IsTrue();

			var positions = maybePositions.AsT0;

			await Assert.That(positions).ContainsKey("help");
			await Assert.That(positions).ContainsKey("newbie");
			await Assert.That(positions).ContainsKey("newbie2");

			// Verify the byte ranges produce correct content when read from file.
			var fileBytes = await File.ReadAllBytesAsync(testFilePath);

			var newbieStart = (int)positions["newbie"].Start;
			var newbieEnd = (int)positions["newbie"].End;
			var newbieContent = System.Text.Encoding.UTF8.GetString(fileBytes, newbieStart, newbieEnd - newbieStart);
			await Assert.That(newbieContent).StartsWith("# newbie\r\n");
			await Assert.That(newbieContent).Contains("Newbie help content");
			await Assert.That(newbieContent).DoesNotContain("main help");

			var newbie2Start = (int)positions["newbie2"].Start;
			var newbie2End = (int)positions["newbie2"].End;
			var newbie2Content = System.Text.Encoding.UTF8.GetString(fileBytes, newbie2Start, newbie2End - newbie2Start);
			await Assert.That(newbie2Content).StartsWith("# newbie2\r\n");
			await Assert.That(newbie2Content).Contains("More newbie content");
			await Assert.That(newbie2Content).DoesNotContain("Newbie help content");
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
	public async Task PositionBasedIndexingHandlesBom()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpBom.md");

		// Write a file with a UTF-8 BOM prefix.
		var content = "# help\nMain help content.\n\n# topic2\nSecond topic.\n";
		var bom = new byte[] { 0xEF, 0xBB, 0xBF };
		var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
		var withBom = new byte[bom.Length + contentBytes.Length];
		bom.CopyTo(withBom, 0);
		contentBytes.CopyTo(withBom, bom.Length);
		await File.WriteAllBytesAsync(testFilePath, withBom);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybePositions = Helpfiles.IndexMarkdownPositions(fileInfo);

			await Assert.That(maybePositions.IsT0).IsTrue();

			var positions = maybePositions.AsT0;

			await Assert.That(positions).ContainsKey("help");
			await Assert.That(positions).ContainsKey("topic2");

			// Verify the byte ranges produce correct content when read from the BOM file.
			var fileBytes = await File.ReadAllBytesAsync(testFilePath);

			var helpStart = (int)positions["help"].Start;
			var helpEnd = (int)positions["help"].End;
			var helpContent = System.Text.Encoding.UTF8.GetString(fileBytes, helpStart, helpEnd - helpStart);
			await Assert.That(helpContent).StartsWith("# help\n");
			await Assert.That(helpContent).Contains("Main help content");

			var topic2Start = (int)positions["topic2"].Start;
			var topic2End = (int)positions["topic2"].End;
			var topic2Content = System.Text.Encoding.UTF8.GetString(fileBytes, topic2Start, topic2End - topic2Start);
			await Assert.That(topic2Content).StartsWith("# topic2\n");
			await Assert.That(topic2Content).Contains("Second topic");
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
	public async Task PositionBasedIndexingHandlesMultipleAliasGroups()
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var testFilePath = Path.Combine(currentDirectory, "TestMarkdownHelpMultiAlias.md");

		var testContent = "# FUNCTIONS2\n  There are two types.\n\n# FUNCTION LIST\n# FUNCTION TYPES\n  Several variants available.\n\n# Attribute functions\n  These access attributes.\n";
		await File.WriteAllTextAsync(testFilePath, testContent);

		try
		{
			var fileInfo = new FileInfo(testFilePath);
			var maybePositions = Helpfiles.IndexMarkdownPositions(fileInfo);

			await Assert.That(maybePositions.IsT0).IsTrue();

			var positions = maybePositions.AsT0;

			// FUNCTION LIST and FUNCTION TYPES share the same range.
			await Assert.That(positions["FUNCTION LIST"].Start).IsEqualTo(positions["FUNCTION TYPES"].Start);
			await Assert.That(positions["FUNCTION LIST"].End).IsEqualTo(positions["FUNCTION TYPES"].End);

			// FUNCTIONS2 has its own range.
			await Assert.That(positions["FUNCTIONS2"].Start).IsNotEqualTo(positions["FUNCTION LIST"].Start);

			// Verify content from byte positions.
			var fileBytes = await File.ReadAllBytesAsync(testFilePath);

			var flStart = (int)positions["FUNCTION LIST"].Start;
			var flEnd = (int)positions["FUNCTION LIST"].End;
			var flContent = System.Text.Encoding.UTF8.GetString(fileBytes, flStart, flEnd - flStart);
			await Assert.That(flContent).Contains("# FUNCTION LIST");
			await Assert.That(flContent).Contains("Several variants");
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
	public async Task RandomSampledTopicsFromRealHelpFilesHaveContent()
	{
		var helpDir = FindHelpfilesDirectory();
		if (helpDir == null)
		{
			// Skip if help files directory not found (e.g. running outside repo).
			return;
		}

		var mdFiles = Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories);
		await Assert.That(mdFiles.Length).IsGreaterThan(0);

		var rng = new Random(42); // Fixed seed for reproducibility.
		var failures = new List<string>();

		foreach (var filePath in mdFiles)
		{
			var fileInfo = new FileInfo(filePath);
			var maybeIndexes = Helpfiles.IndexMarkdown(fileInfo);

			await Assert.That(maybeIndexes.IsT0).IsTrue();

			var indexes = maybeIndexes.AsT0;
			if (indexes.Count == 0)
			{
				continue;
			}

			// Pick up to 5 random topics from this file.
			var topics = indexes.Keys.ToList();
			var sampled = topics.OrderBy(_ => rng.Next()).Take(5).ToList();

			foreach (var topic in sampled)
			{
				var content = indexes[topic];
				if (string.IsNullOrWhiteSpace(content) || content.Trim() == $"# {topic}")
				{
					failures.Add($"{Path.GetFileName(filePath)}: topic '{topic}' has no content beyond the header");
				}
			}
		}

		await Assert.That(failures).IsEmpty();
	}

	private static string? FindHelpfilesDirectory()
	{
		// Walk up from the current directory to find the repository root,
		// then return the Helpfiles/SharpMUSH path.
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir != null)
		{
			var candidate = Path.Combine(dir.FullName, "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
			dir = dir.Parent;
		}
		return null;
	}
}
