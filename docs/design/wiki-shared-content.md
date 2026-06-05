# Wiki as Shared Content — Detailed Design

## Core Principle

A wiki page is a single source of truth. It is authored in Markdown, stored in the
database, and rendered to two output formats:

1. **HTML** — for the web portal (via Markdig)
2. **ANSI text** — for the in-game terminal (via a custom Markdown-to-MString renderer)

Both outputs come from the same source document. Edits from either interface update
the same record. Changes propagate in real-time via NATS → SignalR (web) and
NATS → game notify (in-game).

## Data Model

### WikiPage (graph node)

```csharp
public record WikiPage
{
    public required string Slug { get; init; }         // URL/command identifier
    public required string Title { get; init; }        // Display name
    public required string Body { get; init; }         // Markdown source
    public string? Category { get; init; }             // Grouping key
    public string[] Tags { get; init; } = [];          // Searchable metadata
    public DBRef Author { get; init; }                 // Creator
    public DBRef LastEditor { get; init; }             // Most recent editor
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool Locked { get; init; }                  // Admin-only editing
    public bool Published { get; init; } = true;       // Visible to non-admins
    public int SortOrder { get; init; }                // Within category
    public int Revision { get; init; }                 // Monotonic version counter
}
```

### WikiRevision (for history/diff)

```csharp
public record WikiRevision
{
    public required string Slug { get; init; }
    public int Revision { get; init; }
    public required string Body { get; init; }         // Full snapshot
    public DBRef Editor { get; init; }
    public DateTime Timestamp { get; init; }
    public string? EditSummary { get; init; }
}
```

### Graph Relationships

```
(WikiPage)-[:LINKS_TO]->(WikiPage)       // Inter-page links
(WikiPage)-[:REFERENCES]->(SharpObject)  // Links to game objects
(WikiPage)-[:AUTHORED_BY]->(Player)
(WikiPage)-[:CATEGORIZED_IN]->(WikiCategory)
```

## Markdown Extensions (SharpMUSH-specific)

Beyond standard CommonMark + GFM, these custom extensions are recognized:

### Wiki Links
```markdown
[[character-creation]]              → link to wiki page (uses page title as text)
[[character-creation|CharGen]]      → link with custom display text
[[#section-heading]]                → same-page anchor link
[[other-page#section]]              → cross-page anchor link
```

### Dynamic Includes
```markdown
{{online-players}}                  → live count of connected players
{{recent-scenes:5}}                 → 5 most recent scene summaries
{{character:Gandalf:quote}}         → pulls the 'quote' attribute from character
{{object:#123:desc}}                → pulls desc attribute from object #123
{{toc}}                             → auto-generated table of contents
{{category:rules}}                  → lists all pages in 'rules' category
```

### Game Integration Blocks
```markdown
:::chargen
This content only appears during character creation flow.
:::

:::admin
This content only appears for admin-level users.
:::

:::mush-only
This content only renders in-game (skipped on web).
:::

:::web-only
This content only renders on web portal (skipped in-game).
:::
```

### Callout Blocks (render as colored boxes on web, prefixed text in-game)
```markdown
> [!note] Important
> This is a note callout.

> [!warning] Danger
> This is a warning.

> [!tip] Helpful Hint
> This is a tip.
```

## Rendering Pipeline

### Web (Markdig)

```csharp
public class WikiRenderer
{
    private readonly MarkdownPipeline _pipeline;
    
    public WikiRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()        // Tables, task lists, etc.
            .Use<WikiLinkExtension>()       // [[page]] → <a href="/wiki/page">
            .Use<DynamicIncludeExtension>() // {{template}} → resolved content
            .Use<GameBlockExtension>()      // :::admin, :::web-only, etc.
            .Use<CalloutExtension>()        // > [!note] → styled div
            .Build();
    }
    
    public string RenderHtml(string markdown, WikiRenderContext context)
    {
        // context carries: current user permissions, object resolver, etc.
        return Markdown.ToHtml(markdown, _pipeline);
    }
}
```

### In-Game (Markdown → MString/ANSI)

```csharp
public class WikiAnsiRenderer
{
    /// Converts Markdown to an MString (MarkupString) with ANSI formatting.
    /// This is the in-game rendering path.
    public MString RenderAnsi(string markdown, WikiRenderContext context)
    {
        var doc = Markdown.Parse(markdown);
        var builder = new MStringBuilder();
        
        foreach (var block in doc)
        {
            RenderBlock(block, builder, context, depth: 0);
        }
        
        return builder.Build();
    }
    
    private void RenderBlock(Block block, MStringBuilder builder, 
                             WikiRenderContext ctx, int depth)
    {
        switch (block)
        {
            case HeadingBlock h:
                // # → bold+cyan, ## → bold, ### → underline
                var style = h.Level switch
                {
                    1 => AnsiStyle.BoldCyan,
                    2 => AnsiStyle.Bold,
                    _ => AnsiStyle.Underline
                };
                builder.AppendStyled(style, GetInlineText(h.Inline));
                builder.AppendLine();
                if (h.Level <= 2)
                    builder.AppendLine(new string('─', 60));
                break;
                
            case ParagraphBlock p:
                builder.Append(RenderInlines(p.Inline, ctx));
                builder.AppendLine();
                builder.AppendLine();
                break;
                
            case ListBlock list:
                int i = 1;
                foreach (var item in list)
                {
                    var prefix = list.IsOrdered ? $"  {i++}. " : "  • ";
                    builder.Append(prefix);
                    // render item content
                    foreach (var sub in (ListItemBlock)item)
                        RenderBlock(sub, builder, ctx, depth + 1);
                }
                break;
                
            case QuoteBlock q:
                // Render with │ border character and indent
                foreach (var sub in q)
                {
                    builder.Append("  │ ");
                    RenderBlock(sub, builder, ctx, depth + 1);
                }
                break;
                
            case FencedCodeBlock code:
                builder.AppendLine("┌" + new string('─', 58) + "┐");
                foreach (var line in code.Lines)
                    builder.AppendStyled(AnsiStyle.Yellow, "│ " + line + "\n");
                builder.AppendLine("└" + new string('─', 58) + "┘");
                break;
                
            case ThematicBreakBlock:
                builder.AppendLine(new string('═', 60));
                break;
        }
    }
    
    private MString RenderInlines(ContainerInline inlines, WikiRenderContext ctx)
    {
        var builder = new MStringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case EmphasisInline em:
                    var s = em.DelimiterCount == 2 ? AnsiStyle.Bold : AnsiStyle.Underline;
                    builder.AppendStyled(s, GetText(em));
                    break;
                case CodeInline code:
                    builder.AppendStyled(AnsiStyle.Yellow, code.Content);
                    break;
                case LinkInline link:
                    if (ctx.MxpEnabled)
                        builder.AppendMxpLink(link.Url, GetText(link));
                    else
                        builder.Append($"{GetText(link)} ({link.Url})");
                    break;
                case WikiLinkInline wl:
                    if (ctx.MxpEnabled)
                        builder.AppendMxpSend($"wiki {wl.Slug}", wl.DisplayText ?? wl.Title);
                    else
                        builder.Append($"{wl.DisplayText ?? wl.Title} [wiki {wl.Slug}]");
                    break;
                case LiteralInline lit:
                    builder.Append(lit.Content.ToString());
                    break;
            }
        }
        return builder.Build();
    }
}
```

## In-Game Commands

### +wiki <slug>

Displays a wiki page rendered to ANSI. Pagination for long pages (like `help`).

```
> +wiki character-creation

═══════════════════════════════════════════════════════════
CHARACTER CREATION
═══════════════════════════════════════════════════════════

Welcome to character creation! This guide walks you through the
process of building your character for the game.

STEP 1: CONCEPT
──────────────────────────────────────────────────────────

Think about who your character is...

  • What is their name?
  • Where are they from?
  • What drives them?

  [!] NOTE: All characters must be approved by staff before play.

See also: wiki backgrounds, wiki attributes
═══════════════════════════════════════════════════════════
```

### +wiki/list [category]

```
> +wiki/list rules

WIKI PAGES — RULES (7 pages)
──────────────────────────────────────────────────────────
  character-creation    Character Creation         (updated 2d ago)
  combat-rules         Combat System              (updated 1w ago)
  magic-system         Magic & Spellcasting       (updated 3d ago)
  ooc-policies         OOC Policies               (updated 2w ago)
  ...
```

### +wiki/search <terms>

```
> +wiki/search magic spell

WIKI SEARCH: "magic spell" (3 results)
──────────────────────────────────────────────────────────
  magic-system         Magic & Spellcasting
    ...learn a new spell, the character must...
  
  character-creation   Character Creation
    ...magic users should select their spell list during...
  
  faq                  Frequently Asked Questions
    ...Q: How do I cast a spell? A: Use +cast...
```

### +wiki/edit <slug>=<body>

Permission-gated. Opens the page for editing. For long content, a two-step flow:
1. `+wiki/edit combat-rules` — shows current content + "use +wiki/replace to set"
2. `+wiki/replace combat-rules=<full new markdown body>`

Or for small edits, a `+wiki/append` command.

### Softcode Functions

```
wiki(slug)                → returns full markdown body
wiki(slug, title)         → returns page title
wiki(slug, category)      → returns category name
wiki(slug, author)        → returns author dbref
wiki(slug, updated)       → returns last update timestamp
wiki(slug, exists)        → returns 1/0
wikilist()                → all slugs, space-separated
wikilist(category)        → slugs in category
wikisearch(terms)         → matching slugs
wikirender(slug)          → returns ANSI-rendered content (for embedding in descs)
```

## Real-Time Sync

### Edit Flow (Web → Game)

```
1. Player edits wiki page on web portal
2. WikiController.Put() saves to DB, increments revision
3. Controller publishes to NATS: "wiki.updated" { slug, revision, editor }
4. SignalR hub picks up NATS message, broadcasts to "wiki:{slug}" group
5. Any web client viewing that page gets a "page updated" signal → auto-refresh
6. Game engine's wiki cache (if any) is invalidated
7. Next in-game +wiki command fetches fresh content
```

### Edit Flow (Game → Web)

```
1. Player uses +wiki/edit in-game
2. WikiEditCommandHandler saves to DB, increments revision
3. Handler publishes to NATS: "wiki.updated" { slug, revision, editor }
4. SignalR hub broadcasts to all web viewers of that page
5. Web portal shows "This page was updated by [player]. Refresh?"
   (or auto-refreshes if the viewer isn't in edit mode)
```

### Conflict Resolution

Simple last-write-wins with warning:
- If two editors save simultaneously, the later save wins
- The earlier editor gets a notification: "Your edit to [page] was superseded by [other player]"
- Full revision history means nothing is lost — diffs show what changed
- Future enhancement: operational transform or CRDT for live collaborative editing

## Relationship to Help Files

### Migration Path

PennMUSH-style help files (`help.txt`, `news.txt`, `rules.txt`) can be imported:

```csharp
public class HelpFileImporter
{
    // PennMUSH help format:
    // & topic-name
    // Content lines...
    // & next-topic
    
    public IEnumerable<WikiPage> Import(string helpFileContent, string category)
    {
        var entries = ParseHelpFile(helpFileContent);
        foreach (var (topic, body) in entries)
        {
            yield return new WikiPage
            {
                Slug = Slugify(topic),
                Title = TitleCase(topic),
                Body = ConvertToMarkdown(body),  // minimal: just wraps in paragraphs
                Category = category,
                Published = true
            };
        }
    }
}
```

### Backward Compatibility

The existing `help` command can be wired to the wiki:
```
help <topic>  →  equivalent to  +wiki <topic>
```

Or the game can maintain both systems (traditional help + wiki) with the wiki being
the "modern" path and help files being a read-only fallback for topics not yet migrated.

## Search

### Full-Text Search (Database Layer)

ArangoDB: Use ArangoSearch (built-in) with an analyzer for English stemming:
```aql
FOR page IN wikiPagesView
  SEARCH ANALYZER(page.body IN TOKENS(@query, "text_en"), "text_en")
  SORT BM25(page) DESC
  LIMIT 20
  RETURN { slug: page.slug, title: page.title, score: BM25(page) }
```

SurrealDB: Native full-text search via `SEARCH` keyword.
Memgraph: Would need an external search index (or in-memory Lucene.NET).

### In-Game Search

The `wikisearch()` function and `+wiki/search` command use the same DB-level
full-text search. Results are formatted for the terminal with context snippets.

## Permissions Matrix

| Action              | Guest (web) | Player   | Builder  | Admin    |
|---------------------|-------------|----------|----------|----------|
| Read published page | ✓           | ✓        | ✓        | ✓        |
| Read unpublished    | ✗           | ✗        | ✓        | ✓        |
| Create page         | ✗           | config*  | ✓        | ✓        |
| Edit unlocked page  | ✗           | config*  | ✓        | ✓        |
| Edit locked page    | ✗           | ✗        | ✗        | ✓        |
| Delete page         | ✗           | ✗        | ✗        | ✓        |
| Lock/unlock page    | ✗           | ✗        | ✗        | ✓        |
| View history        | ✗           | ✓        | ✓        | ✓        |
| Revert to revision  | ✗           | ✗        | ✓        | ✓        |

*config = controlled by a game-wide setting: `wiki.player_edit_enabled` (bool)

## File Attachments

Wiki pages can reference uploaded files (images, PDFs, etc.):

```markdown
![Map of the city](/wiki/files/city-map.png)
[Download the rulebook](/wiki/files/rulebook.pdf)
```

Files stored via a simple blob store (filesystem or DB BLOBs). The server serves
them at `/wiki/files/{filename}`. In-game, file references are rendered as:
```
[Image: city-map.png — view on web portal]
[File: rulebook.pdf — view on web portal]
```

## Configuration Options (SharpMUSHOptions)

```csharp
public record WikiOptions(
    [property: SharpConfig("Player Editing", "Allow non-builder players to create/edit wiki pages",
        Category = "Content", Group = "Wiki")]
    bool PlayerEditEnabled = false,
    
    [property: SharpConfig("Guest Reading", "Allow unauthenticated web visitors to read wiki",
        Category = "Content", Group = "Wiki")]
    bool GuestReadEnabled = true,
    
    [property: SharpConfig("Max Page Size", "Maximum wiki page body size in characters",
        Category = "Content", Group = "Wiki", Min = 1000, Max = 500000)]
    int MaxPageSize = 50000,
    
    [property: SharpConfig("Revision History", "Number of revisions to keep per page (0 = unlimited)",
        Category = "Content", Group = "Wiki", Min = 0, Max = 1000)]
    int MaxRevisions = 50,
    
    [property: SharpConfig("Help Redirect", "Redirect 'help <topic>' to wiki if page exists",
        Category = "Content", Group = "Wiki")]
    bool HelpRedirectToWiki = false
);
```
