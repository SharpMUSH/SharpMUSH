using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Whether a reader can get to every help topic by following the index. A topic no list links to is
/// found only by guessing its name: 93 were, including half the lock types, most event groups and
/// every <c>@config</c> category, because their index pages named them as plain text.
/// </summary>
public class HelpNavigationTests
{
	private static readonly MarkdownPipeline Pipeline =
		RecursiveMarkdownHelper.ConfigureHelpSyntax(new MarkdownPipelineBuilder()).Build();

	private sealed record Corpus(IReadOnlyDictionary<string, HelpArticle> ByLookup, IReadOnlyList<HelpArticle> Articles)
	{
		public HelpArticle? Find(string lookup) => ByLookup.GetValueOrDefault(lookup);

		public IEnumerable<HelpArticle> Linked(HelpArticle article) =>
			Markdown.Parse(article.Markdown, Pipeline).Descendants<LinkInline>()
				.Where(link => link.GetData(HelpTopicInlineParser.CommandDataKey) is true)
				.Select(link => Find(link.Url![5..]))
				.OfType<HelpArticle>();
	}

	private static Corpus Help()
	{
		var parsed = TestPaths.Helpfiles.GetFiles("*.md")
			.OrderBy(file => file.Name, StringComparer.Ordinal)
			.SelectMany(file => HelpArticleParser.Parse(File.ReadAllText(file.FullName), "help"))
			.ToList();
		var byLookup = new Dictionary<string, HelpArticle>(StringComparer.OrdinalIgnoreCase);
		foreach (var (article, redirects, _, _) in parsed)
		{
			foreach (var name in article.Aliases.Prepend(article.Lookup)
				.Concat(article.Sections.SelectMany(section => section.Aliases.Prepend(section.Lookup)))
				.Concat(redirects.Keys))
			{
				byLookup.TryAdd(name, article);
			}
		}
		return new Corpus(byLookup, parsed.Select(article => article.Article).ToList());
	}

	private static HashSet<string> Reachable(Corpus help, HelpArticle start)
	{
		var seen = new HashSet<string> { start.Id };
		var pending = new Stack<HelpArticle>([start]);
		while (pending.TryPop(out var article))
		{
			foreach (var next in help.Linked(article).Where(next => seen.Add(next.Id)))
			{
				pending.Push(next);
			}
		}
		return seen;
	}

	[Test]
	public async Task EveryTopicIsReachableFromTheHelpIndex()
	{
		var help = Help();
		var reachable = Reachable(help, help.Find("help")!);

		var unreachable = help.Articles
			.Where(article => !reachable.Contains(article.Id))
			.Select(article => article.Lookup)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(unreachable).IsEmpty();
	}

	/// <summary>
	/// Every <c>[topic]</c> link in every help file, old-style aggregates included, names a topic that exists. The
	/// integrity check covers only declared articles, so a dead link in an aggregate showed the reader
	/// <c>No entry for</c> until the topic moved into an article of its own.
	/// </summary>
	[Test]
	public async Task EveryHelpLinkResolves()
	{
		var help = Help();
		var dead = help.Articles
			.SelectMany(article => Markdown.Parse(article.Markdown, Pipeline).Descendants<LinkInline>()
				.Where(link => link.GetData(HelpTopicInlineParser.CommandDataKey) is true)
				.Select(link => link.Url![5..])
				.Where(topic => help.Find(topic) is null)
				.Select(topic => $"{article.Lookup}: [{topic}]"))
			.Distinct()
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(string.Join("\n", dead)).IsEmpty();
	}

	/// <summary>
	/// Every function is in a category of <c>help function list</c>, which is how PennMUSH's help
	/// is browsed and the only list of what a game can do with, say, a dbref.
	/// </summary>
	[Test]
	public async Task EveryRegisteredFunctionIsListedInACategory()
	{
		var help = Help();
		var listed = help.Linked(help.Find("function list")!)
			.SelectMany(help.Linked)
			.Select(article => article.Id)
			.ToHashSet();

		var unlisted = RegistryInventory.Functions
			.Select(function => help.Find($"{function.Name}()"))
			.OfType<HelpArticle>()
			.Where(article => !listed.Contains(article.Id))
			.Select(article => article.Lookup)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(unlisted).IsEmpty();
	}

	/// <summary>
	/// Every command is in <c>help commands</c> or one of the lists it links: the @-command groups and
	/// the socket commands.
	/// </summary>
	[Test]
	public async Task EveryRegisteredCommandIsListed()
	{
		var help = Help();
		var index = help.Find("commands")!;
		var lists = help.Linked(index).Prepend(index).ToList();
		var listed = lists.SelectMany(help.Linked).Select(article => article.Id).ToHashSet();

		var unlisted = RegistryInventory.Commands
			.Select(command => help.Find(command.Name))
			.OfType<HelpArticle>()
			.Where(article => !listed.Contains(article.Id))
			.Select(article => article.Lookup)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(unlisted).IsEmpty();
	}
}
