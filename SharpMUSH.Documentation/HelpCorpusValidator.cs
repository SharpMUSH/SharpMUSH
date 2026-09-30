using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation;

/// <summary>Strict integrity checks for declared articles; legacy debt remains advisory.</summary>
public static class HelpCorpusValidator
{
	public static void Validate(IReadOnlyList<HelpArticleParser.ParsedArticle> articles)
	{
		var names = new Dictionary<string, (string Canonical, bool Declared, bool Hidden)>(StringComparer.OrdinalIgnoreCase);
		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var parsed in articles)
		{
			var article = parsed.Article;
			if (parsed.Declared && !ids.Add(article.Id))
			{
				throw new InvalidDataException($"Duplicate article ID: {article.Id}");
			}
			void Add(string name, string canonical, bool hidden = false)
			{
				if (!names.TryAdd(name, (canonical, parsed.Declared, hidden))
					&& (parsed.Declared || names[name].Declared))
				{
					throw new InvalidDataException($"Duplicate help lookup: {name}");
				}
			}
			Add(article.Lookup, article.Lookup);
			foreach (var alias in article.Aliases)
			{
				Add(alias, article.Lookup);
			}
			foreach (var section in article.Sections)
			{
				Add(section.Lookup, section.Lookup);
				foreach (var alias in section.Aliases)
				{
					Add(alias, section.Lookup);
				}
			}
			foreach (var (alias, target) in parsed.Redirects)
			{
				Add(alias, target, true);
			}
		}
		var pipeline = RecursiveMarkdownHelper.ConfigureHelpSyntax(new MarkdownPipelineBuilder()).Build();
		var canonicalNames = names.Where(pair => pair.Key.Equals(pair.Value.Canonical, StringComparison.OrdinalIgnoreCase))
			.Select(pair => pair.Key.Replace("()", string.Empty)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var name in canonicalNames)
		{
			var numberless = Regex.Replace(name, "[0-9]+$", string.Empty);
			if (numberless != name && canonicalNames.Contains(numberless))
			{
				throw new InvalidDataException($"Numbered continuation must be a named section: {name}");
			}
		}
		foreach (var parsed in articles.Where(article => article.Declared))
		{
			var links = Markdown.Parse(parsed.Article.Markdown, pipeline).Descendants<LinkInline>()
				.Where(link => link.GetData(HelpTopicInlineParser.CommandDataKey) is true)
				.Select(link => link.Url![5..]).Concat(parsed.Links);
			foreach (var target in links)
			{
				if (!names.TryGetValue(target, out var resolved) || resolved.Hidden)
				{
					throw new InvalidDataException($"Unresolved or deprecated help link in {parsed.Article.Id}: {target}");
				}
			}
		}
	}
}
