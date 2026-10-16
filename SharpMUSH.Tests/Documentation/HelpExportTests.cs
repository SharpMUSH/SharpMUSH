using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Whether the help export the documentation sites publish is the help the game serves: the engine's and every
/// shipped plugin's, with the plugins listed on the index topics they name.
/// </summary>
public class HelpExportTests
{
	private static readonly SharpMUSHOptions BaseConfig = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	[Test]
	public async Task ExportHoldsExactlyTheArticlesTheServerServes()
	{
		var repository = new DirectoryInfo(TestPaths.RepositoryRoot);
		var plugins = HelpExport.ShippedPlugins(repository);
		await Assert.That(plugins).IsNotEmpty();

		var root = Path.Join(Path.GetTempPath(), $"sharpmush-help-export-{Guid.NewGuid():N}");
		try
		{
			var text = Directory.CreateDirectory(Path.Join(root, "text", HelpExport.Corpus));
			foreach (var file in HelpExport.EngineHelp(repository).GetFiles("*.md"))
			{
				file.CopyTo(Path.Join(text.FullName, file.Name));
			}
			// A copy of each plugin's help alone, as an installed plugin's folder holds it; the project folder's bin
			// and obj would each be indexed as a category.
			var entries = plugins.Select(plugin =>
			{
				var folder = Path.Join(root, "plugins", plugin.Id);
				var help = Directory.CreateDirectory(Path.Join(folder, HelpExport.Corpus));
				foreach (var file in plugin.Help.GetFiles("*.md"))
				{
					file.CopyTo(Path.Join(help.FullName, file.Name));
				}
				return new PluginBootEntry(plugin.Id, plugin.Name, "1.0.0", null, PluginOrigin.BuiltIn, folder, PluginBootStatus.Loaded, null);
			}).ToList();
			var options = BaseConfig with
			{
				TextFile = BaseConfig.TextFile with { TextFilesDirectory = Path.Join(root, "text"), CacheOnStartup = false }
			};
			var service = new TextFileService(Options.Create(options), NullLogger<TextFileService>.Instance, PluginCatalog.ForEntries(entries));
			await service.ReindexAsync();
			var resolver = new HelpTopicResolver(service);

			var exported = HelpExport.Build(HelpExport.EngineHelp(repository), plugins)
				.SelectMany(file => HelpArticleParser.Parse(file.Markdown, HelpExport.Corpus).Select(parsed => (File: file.Name, parsed.Article)))
				.ToList();
			var differences = new List<string>();
			foreach (var (file, article) in exported)
			{
				var served = (await resolver.GetExactAsync(HelpExport.Corpus, article.Lookup))?.Article;
				if (served is null || Describe(served) != Describe(article))
				{
					differences.Add($"{file}: {article.Lookup} is not exported as it is served");
				}
			}
			var ids = exported.Select(item => item.Article.Id).ToHashSet(StringComparer.Ordinal);
			foreach (var topic in await resolver.ListTopicsAsync(HelpExport.Corpus))
			{
				if ((await resolver.GetExactAsync(HelpExport.Corpus, topic))?.Article is not { } article || !ids.Contains(article.Id))
				{
					differences.Add($"{topic} is served but not exported");
				}
			}

			await Assert.That(string.Join("\n", differences)).IsEmpty();
			await Assert.That(exported.Single(item => item.Article.Lookup == HelpListings.PluginHelpLookup).Article.Overview)
				.Contains($"{plugins[0].Name}: [")
				.Because("plugin help lists each plugin's topics under its name");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static string Describe(HelpArticle article) =>
		string.Join("\n---\n", [article.Id, article.Title, string.Join("|", article.Aliases), article.Overview,
			.. article.Sections.Select(section => $"{section.Id}|{section.Lookup}|{string.Join("|", section.Aliases)}\n{section.Markdown}")]);
}
