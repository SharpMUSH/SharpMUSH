using SharpMUSH.Library.Plugins;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Documentation;

/// <summary>
/// The general help as the game serves it, for the documentation sites: the engine's help files and every plugin's,
/// with the plugin listings (<see cref="HelpListings"/>) written into the index topics they belong on. The sites read
/// this instead of the source folders, which hold neither the plugins' help nor the listings.
/// </summary>
/// <remarks>
/// Each file keeps its name and its own Markdown; a listing is inserted where the server appends it, so parsing an
/// exported file gives the articles the server indexes. A plugin file is left out of the game when it does not
/// index; the export refuses instead, since a page the site silently drops is what it exists to prevent.
/// </remarks>
public static class HelpExport
{
	/// <summary>The general help corpus, the only one the sites publish.</summary>
	public const string Corpus = "help";

	/// <summary>The folder the help files are written to, under the output directory.</summary>
	public const string HelpFolder = "help";

	/// <summary>The manifest of every exported article, under the output directory.</summary>
	public const string ManifestFile = "articles.json";

	/// <summary>A plugin whose help the export carries.</summary>
	/// <param name="Id">The plugin's id, recorded on each of its articles.</param>
	/// <param name="Name">The name its listing on <see cref="HelpListings.PluginHelpLookup"/> goes under.</param>
	/// <param name="Help">The folder holding its general help, <c>&lt;plugin&gt;/help</c>.</param>
	public sealed record Plugin(string Id, string Name, DirectoryInfo Help);

	/// <summary>One exported help file.</summary>
	/// <param name="Name">Its file name.</param>
	/// <param name="Plugin">The plugin it came from, or null for the engine's.</param>
	/// <param name="Markdown">Its contents, listings included.</param>
	public sealed record ExportedFile(string Name, string? Plugin, string Markdown);

	public sealed record ManifestSection(string Id, string Lookup, string Heading, IReadOnlyList<string> Aliases);

	/// <summary>One article in <see cref="ManifestFile"/>.</summary>
	public sealed record ManifestArticle(string Id, string Lookup, string Title, IReadOnlyList<string> Aliases,
		IReadOnlyList<ManifestSection> Sections, string File, string? Plugin);

	private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	/// <summary>The engine's general help folder in a checkout of the repository.</summary>
	public static DirectoryInfo EngineHelp(DirectoryInfo repository) =>
		new(Path.Join(repository.FullName, "SharpMUSH.Documentation", "Helpfiles", "SharpMUSH"));

	/// <summary>
	/// The plugins in a checkout of the repository that ship general help: each <c>SharpMUSH.Plugins.*</c> folder with a
	/// <c>plugin.json</c> and a <c>help</c> folder, named as its manifest names it, in id order.
	/// </summary>
	public static IReadOnlyList<Plugin> ShippedPlugins(DirectoryInfo repository) =>
		repository.GetDirectories("SharpMUSH.Plugins.*")
			.Where(folder => File.Exists(Path.Join(folder.FullName, PluginManifest.FileName))
				&& Directory.Exists(Path.Join(folder.FullName, HelpFolder)))
			.Select(folder =>
			{
				var manifest = JsonSerializer.Deserialize<PluginManifest>(
					File.ReadAllText(Path.Join(folder.FullName, PluginManifest.FileName)), ManifestOptions)
					?? throw new InvalidDataException($"{folder.Name}/{PluginManifest.FileName} is empty.");
				return new Plugin(manifest.Id, manifest.Name ?? manifest.Id, new DirectoryInfo(Path.Join(folder.FullName, HelpFolder)));
			})
			.OrderBy(plugin => plugin.Id, StringComparer.Ordinal)
			.ToList();

	/// <summary>Builds the export from the engine's general help folder and the plugins', in plugin load order.</summary>
	/// <exception cref="InvalidDataException">A file does not parse, a plugin topic or file name clashes with one already
	/// exported, or a listing names a topic that is not there.</exception>
	public static IReadOnlyList<ExportedFile> Build(DirectoryInfo engine, IReadOnlyList<Plugin> plugins)
	{
		var sources = new List<(ExportedFile File, IReadOnlyList<HelpArticleParser.ParsedArticle> Articles)>();
		var topics = new Dictionary<string, (int Source, HelpArticleParser.ParsedArticle Article, string? SectionId)>(StringComparer.OrdinalIgnoreCase);

		void Add(FileInfo file, string? plugin)
		{
			if (sources.Any(source => source.File.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidDataException($"{plugin}'s help file {file.Name} has the name of one already exported.");
			}
			var markdown = File.ReadAllText(file.FullName).TrimStart('\uFEFF');
			var parsed = HelpArticleParser.Parse(markdown, Corpus);
			var source = sources.Count;
			foreach (var article in parsed)
			{
				var lookups = new[] { (Lookup: article.Article.Lookup, SectionId: (string?)null) }
					.Concat(article.Article.Aliases.Select(alias => (alias, (string?)null)))
					.Concat(article.Article.Sections.SelectMany(section => new[] { section.Lookup }.Concat(section.Aliases)
						.Select(lookup => (lookup, (string?)section.Id))));
				foreach (var (lookup, sectionId) in lookups)
				{
					// The server keeps the first of two engine topics with one name; a plugin may not repeat one at all.
					if (!topics.TryAdd(lookup, (source, article, sectionId)) && plugin is not null)
					{
						throw new InvalidDataException($"{plugin}'s help topic {lookup} in {file.Name} is already a help topic.");
					}
				}
			}
			sources.Add((new ExportedFile(file.Name, plugin, markdown), parsed));
		}

		foreach (var file in engine.GetFiles("*.md").OrderBy(file => file.Name, StringComparer.Ordinal))
		{
			Add(file, null);
		}
		var help = new List<HelpListings.PluginHelp>();
		foreach (var plugin in plugins)
		{
			var first = sources.Count;
			foreach (var file in plugin.Help.GetFiles("*.md").OrderBy(file => file.Name, StringComparer.Ordinal))
			{
				Add(file, plugin.Id);
			}
			help.Add(new HelpListings.PluginHelp(plugin.Name, sources.Skip(first).SelectMany(source => source.Articles).ToList()));
		}

		var insertions = sources.Select(_ => new List<(int At, string Text)>()).ToList();
		foreach (var (index, listed, text) in HelpListings.For(help))
		{
			if (!topics.TryGetValue(index, out var target))
			{
				throw new InvalidDataException($"Plugin help for {string.Join(", ", listed)} names {index}, which is not a help topic.");
			}
			var end = target.SectionId is { } sectionId ? target.Article.SectionEnds[sectionId] : target.Article.OverviewEnd;
			var markdown = sources[target.Source].File.Markdown;
			while (end > 0 && char.IsWhiteSpace(markdown[end - 1]))
			{
				end--;
			}
			insertions[target.Source].Add((end, text));
		}

		return sources.Select((source, i) =>
		{
			var markdown = new StringBuilder(source.File.Markdown);
			// From the end, so an insertion does not move the places of those before it.
			foreach (var (at, text) in insertions[i].OrderByDescending(insertion => insertion.At))
			{
				markdown.Insert(at, text);
			}
			return source.File with { Markdown = markdown.ToString() };
		}).ToList();
	}

	/// <summary>The manifest of <paramref name="files"/>: every article, in file order, with the file it is in.</summary>
	public static string Manifest(IEnumerable<ExportedFile> files) =>
		JsonSerializer.Serialize(files.SelectMany(file => HelpArticleParser.Parse(file.Markdown, Corpus).Select(parsed =>
		{
			var article = parsed.Article;
			return new ManifestArticle(article.Id, article.Lookup, article.Title, article.Aliases,
				article.Sections.Select(section => new ManifestSection(section.Id, section.Lookup, section.Heading, section.Aliases)).ToList(),
				file.Name, file.Plugin);
		})), JsonOptions) + "\n";

	/// <summary>Writes <paramref name="files"/> to <c>help/</c> under <paramref name="output"/>, and the manifest beside it.</summary>
	public static void Write(IReadOnlyList<ExportedFile> files, DirectoryInfo output)
	{
		var folder = Directory.CreateDirectory(Path.Join(output.FullName, HelpFolder));
		foreach (var file in files)
		{
			File.WriteAllText(Path.Join(folder.FullName, file.Name), file.Markdown);
		}
		File.WriteAllText(Path.Join(output.FullName, ManifestFile), Manifest(files));
	}
}
