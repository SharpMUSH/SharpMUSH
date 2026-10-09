namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>A stable, corpus-scoped article, shared by all help presentations.</summary>
public sealed record HelpArticle(
	string Corpus,
	string Id,
	string Lookup,
	string Title,
	string Overview,
	IReadOnlyList<string> Aliases,
	IReadOnlyList<HelpSection> Sections)
{
	public string Markdown => Overview + string.Concat(Sections.Select(section => "\n\n" + section.Markdown));

	public HelpEntry Entry(HelpSection? section = null)
	{
		var command = Corpus;
		// An overview that already lists a section, as `[<lookup>]` with a line saying what it holds, is its own
		// directory for that section; repeating it as a bare command would show the reader every section twice.
		var unlisted = Sections.Where(child => !Overview.Contains($"[{child.Lookup}]", StringComparison.OrdinalIgnoreCase)).ToList();
		var navigation = section is null
			? (unlisted.Count == 0 ? string.Empty : "\n") + string.Concat(unlisted.Select(child => $"\n- `{command} {child.Lookup}`"))
			: $"\n\n`{command} {Lookup}`";
		return new HelpEntry(section?.Lookup ?? Lookup, (section?.Markdown ?? Overview) + navigation)
		{
			Article = this,
			SectionId = section?.Id
		};
	}
}

/// <summary>One independently useful lookup. List order is declared reading order.</summary>
public sealed record HelpSection(string Id, string Lookup, string Heading, string Markdown, IReadOnlyList<string> Aliases);
