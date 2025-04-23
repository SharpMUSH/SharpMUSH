using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using SharpMUSH.Documentation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

public class HelpfileTests
{
	[Test]
	[Arguments("ATTRIBUTE TREES")]
	[Arguments("ATTR TREES")]
	[Arguments("ATTRIB TREES")]
	[Arguments("`")]
	[Arguments("@CEMIT")]
	[Arguments("@NSCEMIT")]
	[Arguments("CEMIT()")]
	[Arguments("NSCEMIT()")]
	public async Task CanIndex(string expectedIndex)
	{
		var currentDirectory = Directory.GetCurrentDirectory();
		var dirString = Path.Combine(currentDirectory, "Documentation", "Testfile");
		var helpFiles = new Helpfiles(new DirectoryInfo(dirString));

		helpFiles.Index();

		await Assert.That(helpFiles.IndexedHelp).ContainsKey(expectedIndex);
	}

	[Test]
	[Arguments("pennattr.txt", new[] { "ATTRIBUTE TREES", "ATTR TREES", "ATTRIB TREES", "`" })]
	[Arguments("pennchat.txt", new[] { "@CEMIT", "@NSCEMIT", "CEMIT()", "NSCEMIT()" })]
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

	[Test, Skip("Not yet done.")]
	public async Task MarkdownToMarkup()
	{
		var container = new MarkupStringContainer
		{
			Str = MModule.empty(),
			Inline = false
		};

		var pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).Build();
		var renderer = new MarkdownToAsciiRenderer(container);
		pipeline.Setup(renderer);
		
		var markdown = "# Header1 *Bolded*<br/>Test";
		var doc = Markdown.Parse(markdown, pipeline);
		container = (MarkupStringContainer)renderer.Render(doc);
		
		Console.WriteLine(container.Str.ToString());
		
		await Assert.That(container.Str.ToString()).IsEqualTo(MModule.single("a").ToString());
	}
}