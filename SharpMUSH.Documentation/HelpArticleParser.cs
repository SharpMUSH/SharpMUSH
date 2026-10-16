using Markdig;
using Markdig.Syntax;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation;

/// <summary>Structural Markdown adapter for both legacy aggregates and declared articles.</summary>
public static class HelpArticleParser
{
	private const string MetadataStart = "<!-- help-article";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	public sealed record SectionMetadata(string Id, string Heading, string Lookup, string[]? Aliases = null,
		string[]? ListedIn = null);
	public sealed record ArticleMetadata(string Corpus, string Id, string Lookup, string[] Aliases,
		SectionMetadata[] Sections, Dictionary<string, string>? Redirects = null, string[]? Links = null,
		string[]? ListedIn = null);
	public sealed record ParsedArticle(HelpArticle Article, IReadOnlyDictionary<string, string> Redirects,
		IReadOnlyList<string> Links, bool Declared)
	{
		/// <summary>The index topics that list this article or one of its sections, from <c>listedIn</c>.</summary>
		public IReadOnlyList<HelpListing> Listings { get; init; } = [];

		/// <summary>
		/// Where the overview ends in the parsed text: text inserted here, after the overview's last non-blank character,
		/// is appended to <see cref="HelpArticle.Overview"/>. The documentation export lists plugin help this way.
		/// </summary>
		public int OverviewEnd { get; init; }

		/// <summary>Where each section ends in the parsed text, by section id, as <see cref="OverviewEnd"/> is for the overview.</summary>
		public IReadOnlyDictionary<string, int> SectionEnds { get; init; } = new Dictionary<string, int>();
	}

	/// <summary>
	/// A topic, <paramref name="Lookup"/>, that asks to be listed on another, <paramref name="Index"/>. A plugin's help
	/// uses it to appear on the engine's own lists, such as <c>@-wizard</c> or a function category.
	/// </summary>
	public sealed record HelpListing(string Lookup, string Index);

	public static IReadOnlyList<HeadingBlock> Headings(string markdown) =>
		Markdown.Parse(markdown).OfType<HeadingBlock>().ToList();

	public static string HeadingText(string markdown, HeadingBlock heading)
	{
		var line = markdown[heading.Span.Start..(heading.Span.End + 1)].Split('\n')[0].Trim();
		return line.TrimStart('#').Trim();
	}

	public static IReadOnlyList<ParsedArticle> Parse(string markdown, string corpus)
	{
		markdown = markdown.TrimStart('\uFEFF');
		var headings = Headings(markdown);
		if (!markdown.StartsWith(MetadataStart, StringComparison.Ordinal))
		{
			return ParseLegacy(markdown, corpus, headings);
		}

		var metadataEnd = markdown.IndexOf("-->", StringComparison.Ordinal);
		if (metadataEnd < 0)
		{
			throw new InvalidDataException("Unclosed help article metadata.");
		}
		var metadata = JsonSerializer.Deserialize<ArticleMetadata>(markdown[MetadataStart.Length..metadataEnd], JsonOptions)
			?? throw new InvalidDataException("Missing help article metadata.");
		if (metadata.Corpus != corpus.Split('.')[0] || !ValidId(metadata.Id) || string.IsNullOrWhiteSpace(metadata.Lookup)
			|| metadata.Sections is null || metadata.Aliases is null)
		{
			throw new InvalidDataException("Invalid article identity or corpus.");
		}
		var titles = headings.Where(heading => heading.Level == 1).ToList();
		if (titles.Count != 1)
		{
			throw new InvalidDataException("A declared article must have exactly one H1.");
		}
		var title = titles[0];
		var indexed = new List<(SectionMetadata Metadata, HeadingBlock Heading)>();
		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var lookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { metadata.Lookup };
		foreach (var alias in metadata.Aliases)
		{
			AddUnique(lookups, alias, "lookup");
		}
		foreach (var section in metadata.Sections)
		{
			if (!ValidId(section.Id) || !ids.Add(section.Id))
			{
				throw new InvalidDataException($"Invalid or duplicate section ID: {section.Id}");
			}
			AddUnique(lookups, section.Lookup, "lookup");
			foreach (var alias in section.Aliases ?? [])
			{
				AddUnique(lookups, alias, "lookup");
			}
			var matches = headings.Where(heading => (heading.Level is 2 or 3)
				&& HeadingText(markdown, heading) == section.Heading).ToList();
			if (matches.Count != 1)
			{
				throw new InvalidDataException($"Section heading must occur exactly once: {section.Heading}");
			}
			indexed.Add((section, matches[0]));
		}
		var undeclared = headings.Where(heading => heading.Level == 2 && !indexed.Any(item => item.Heading == heading)).ToList();
		if (undeclared.Count > 0)
		{
			throw new InvalidDataException($"Every H2 in {metadata.Id} must be declared as a section: "
				+ string.Join(", ", undeclared.Select(heading => HeadingText(markdown, heading))));
		}
		var firstSection = indexed.Count == 0 ? markdown.Length : indexed.Min(item => item.Heading.Span.Start);
		var overview = markdown[title.Span.Start..firstSection].Trim();
		var sectionEnds = indexed.ToDictionary(item => item.Metadata.Id, item => indexed
			.Where(next => next.Heading.Span.Start > item.Heading.Span.Start)
			.Select(next => next.Heading.Span.Start).DefaultIfEmpty(markdown.Length).Min());
		var sections = indexed.Select(item => new HelpSection(item.Metadata.Id, item.Metadata.Lookup, item.Metadata.Heading,
			markdown[item.Heading.Span.Start..sectionEnds[item.Metadata.Id]].Trim(), item.Metadata.Aliases ?? [])).ToList();
		var redirects = metadata.Redirects ?? new Dictionary<string, string>();
		foreach (var (alias, target) in redirects)
		{
			AddUnique(lookups, alias, "redirect");
			if (target != metadata.Lookup && !sections.Any(section => section.Lookup == target))
			{
				throw new InvalidDataException($"Redirect target is not canonical: {target}");
			}
		}
		var listings = (metadata.ListedIn ?? []).Select(index => new HelpListing(metadata.Lookup, index))
			.Concat(metadata.Sections.SelectMany(section => (section.ListedIn ?? []).Select(index => new HelpListing(section.Lookup, index))))
			.ToList();
		if (listings.FirstOrDefault(listing => lookups.Contains(listing.Index)) is { } own)
		{
			throw new InvalidDataException($"An article cannot list itself: {own.Index}");
		}
		return [new ParsedArticle(new HelpArticle(metadata.Corpus, metadata.Id, metadata.Lookup,
			HeadingText(markdown, title), overview, metadata.Aliases, sections), redirects, metadata.Links ?? [], true)
		{
			Listings = listings,
			OverviewEnd = firstSection,
			SectionEnds = sectionEnds
		}];
	}

	private static IReadOnlyList<ParsedArticle> ParseLegacy(string markdown, string corpus, IReadOnlyList<HeadingBlock> headings)
	{
		var titles = headings.Where(heading => heading.Level == 1).ToList();
		var articles = new List<ParsedArticle>();
		var aliases = new List<string>();
		for (var i = 0; i < titles.Count; i++)
		{
			var title = titles[i];
			aliases.Add(HeadingText(markdown, title));
			var end = i + 1 < titles.Count ? titles[i + 1].Span.Start : markdown.Length;
			var body = markdown[(title.Span.End + 1)..end].Trim();
			if (body.Length == 0 && i + 1 < titles.Count)
			{
				continue;
			}
			var lookup = aliases[0];
			var overview = "# " + lookup + "\n" + body;
			articles.Add(new ParsedArticle(new HelpArticle(corpus.Split('.')[0], "legacy:" + lookup, lookup,
				lookup, overview, aliases.Skip(1).ToArray(), []), new Dictionary<string, string>(), [], false)
			{
				OverviewEnd = end
			});
			aliases.Clear();
		}
		return articles;
	}

	private static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, "^[a-z][a-z0-9-]*$");

	private static void AddUnique(HashSet<string> values, string value, string kind)
	{
		if (string.IsNullOrWhiteSpace(value) || !values.Add(value))
		{
			throw new InvalidDataException($"Empty or duplicate {kind}: {value}");
		}
	}
}
