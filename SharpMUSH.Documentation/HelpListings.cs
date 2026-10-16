namespace SharpMUSH.Documentation;

/// <summary>
/// Where plugin help is listed on the engine's index topics, so a reader browsing <c>help</c> finds it: every plugin
/// topic on <see cref="PluginHelpLookup"/> under its plugin's name, and a topic that declares <c>listedIn</c> on each
/// index it names. The server appends these to the topics it indexes; the documentation export writes the same text
/// into the files it publishes, so the sites list what the game lists.
/// </summary>
public static class HelpListings
{
	/// <summary>The topic that lists the help of every plugin, whether or not it is listed elsewhere.</summary>
	public const string PluginHelpLookup = "plugin help";

	/// <summary>One plugin's help, as parsed from its files.</summary>
	/// <param name="Name">The name its listing on <see cref="PluginHelpLookup"/> goes under.</param>
	public sealed record PluginHelp(string Name, IReadOnlyList<HelpArticleParser.ParsedArticle> Articles);

	/// <summary>The text appended to the index topic <paramref name="Index"/>, listing <paramref name="Topics"/>.</summary>
	public sealed record Addition(string Index, IReadOnlyList<string> Topics, string Text);

	/// <summary>What each index topic gets appended, in the order the plugins and their topics come.</summary>
	public static IReadOnlyList<Addition> For(IEnumerable<PluginHelp> plugins)
	{
		var lines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		void Note(string target, string line) =>
			(lines.TryGetValue(target, out var existing) ? existing : lines[target] = []).Add(line);

		var help = plugins.Where(plugin => plugin.Articles.Count > 0).ToList();
		foreach (var plugin in help)
		{
			Note(PluginHelpLookup, $"{plugin.Name}: "
				+ string.Join(", ", plugin.Articles.Select(article => $"[{article.Article.Lookup}]")));
		}
		foreach (var listing in help.SelectMany(plugin => plugin.Articles).SelectMany(article => article.Listings))
		{
			Note(listing.Index, $"[{listing.Lookup}]");
		}

		return lines.Select(pair => new Addition(pair.Key, pair.Value,
			pair.Key.Equals(PluginHelpLookup, StringComparison.OrdinalIgnoreCase)
				? "\n\n" + string.Join("<br>\n", pair.Value)
				: "\n\nFrom plugins: " + string.Join(", ", pair.Value))).ToList();
	}
}
