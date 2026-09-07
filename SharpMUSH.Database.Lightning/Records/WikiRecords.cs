namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// Mirrors <c>SurrealDatabase.WikiPageDbRecord</c>. Timestamps stay <c>string</c> (ISO-8601), matching
/// what the SurrealDB partial itself reads and writes rather than <c>WikiPage</c>'s <c>DateTimeOffset</c>.
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
	public string? Category { get; init; }
	public string[]? Tags { get; init; }
	public bool? Published { get; init; }
	public string? SourceLocale { get; init; }
}

/// <summary>Mirrors <c>SurrealDatabase.WikiRevisionDbRecord</c>.</summary>
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

/// <summary>Mirrors <c>SurrealDatabase.WikiTranslationDbRecord</c>.</summary>
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
