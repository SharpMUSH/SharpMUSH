namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// A stored wiki page. Timestamps stay <c>string</c> (ISO-8601) rather than <c>WikiPage</c>'s
/// <c>DateTimeOffset</c>.
/// </summary>
public sealed record WikiPageRecord
{
	public string Slug { get; init; } = "";
	public string Title { get; init; } = "";
	public string Namespace { get; init; } = "main";
	public string MarkdownSource { get; init; } = "";
	public string RenderedHtml { get; init; } = "";
	public string PlainText { get; init; } = "";
	public string AuthorDbref { get; init; } = "";
	public string LastEditorDbref { get; init; } = "";
	public string CreatedAt { get; init; } = "";
	public string UpdatedAt { get; init; } = "";
	public bool IsProtected { get; init; }
	public int RevisionNumber { get; init; } = 1;
	/// <summary>The page's category keys; absent on a row written before
	/// <c>0012_wiki_categories</c>, which fills it.</summary>
	public string[]? Categories { get; init; }
	public bool? Published { get; init; }
	public string? SourceLocale { get; init; }
}

/// <summary>
/// The two fields a page row carried while a category was part of its identity and tags were set beside
/// the text. Read only by <c>0012_wiki_categories</c>, which merges them into the page's
/// category list.
/// </summary>
public sealed record WikiPageFiledRecord
{
	public string? Category { get; init; }
	public string[]? Tags { get; init; }
}

/// <summary>A stored wiki page revision.</summary>
public sealed record WikiRevisionRecord
{
	public string PageId { get; init; } = "";
	public int RevisionNumber { get; init; }
	public string MarkdownSource { get; init; } = "";
	public string EditorDbref { get; init; } = "";
	public string Timestamp { get; init; } = "";
	public string? EditSummary { get; init; }
	public string? Locale { get; init; }
}

/// <summary>A stored wiki page translation link.</summary>
public sealed record WikiTranslationRecord
{
	public string? PageId { get; init; }
	public string? Locale { get; init; }
	public string? Title { get; init; }
	public string? MarkdownSource { get; init; }
	public string? RenderedHtml { get; init; }
	public string? PlainText { get; init; }
	public string? LastEditorDbref { get; init; }
	public string? CreatedAt { get; init; }
	public string? UpdatedAt { get; init; }
	public bool? Published { get; init; }
	public int? RevisionNumber { get; init; }
}
