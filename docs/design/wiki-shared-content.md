# Wiki as Shared Content — Detailed Design

## Core Principle

A wiki page is a single source of truth. It is authored in Markdown, stored in a
**dedicated wiki collection** (separate from game objects), and rendered to two
output formats:

1. **HTML** — for the web portal (via Markdig)
2. **ANSI text** — for the in-game terminal (via a custom Markdown-to-MString renderer)

Both outputs come from the same source document. Edits from either interface update
the same record. Changes propagate in real-time via NATS → SignalR (web) and
NATS → game notify (in-game).

The wiki is NOT embedded in the game object graph. Wiki pages live in their own
collection/table with their own schema. They can *reference* game objects via DBRef
fields (e.g., author, last editor), but they are not SharpObjects and do not
participate in the object namespace, containment hierarchy, or flag system.

### Why Separate?

- Wiki pages have fundamentally different access patterns (full-text search,
  category browsing, revision history, bulk listing)
- They don't need flags, locks, attributes, or containment
- Keeping them out of the object graph means wiki queries are fast and isolated
- Game object queries don't slow down as wiki content grows
- Cleaner schema — wiki-specific fields (slug, category, revision, published)
  don't pollute the SharpObject model

## Data Model

### Storage (Per-Database Backend)

| Backend   | Wiki Pages             | Wiki Revisions          | Indexes                    |
|-----------|------------------------|-------------------------|----------------------------|
| ArangoDB  | `wiki_pages` collection| `wiki_revisions` coll.  | ArangoSearch view on body  |
| SurrealDB | `wiki_page` table      | `wiki_revision` table   | Native FTS on body         |
| Memgraph  | `:WikiPage` nodes      | `:WikiRevision` nodes   | In-memory text index       |

Note: Even in Memgraph (a graph DB), wiki pages are their own labeled nodes with
no edges into the game object graph. They reference game objects by storing DBRef
values as properties, not via graph edges.

### WikiPage Schema

```csharp
/// <summary>
/// A wiki page. Stored in a dedicated collection, separate from game objects.
/// </summary>
public record WikiPage
{
    /// URL-friendly identifier. Unique. Used in commands and URLs.
    /// Format: [a-z0-9-/]+ (allows path-like slugs: "rules/combat")
    public required string Slug { get; init; }
    
    /// Human-readable display title.
    public required string Title { get; init; }
    
    /// Markdown content — the single source of truth.
    public required string Body { get; init; }
    
    /// Top-level grouping (e.g., "rules", "lore", "systems", "staff").
    /// Slugs can use path prefixes as sub-categories: "lore/places/tavern"
    public string? Category { get; init; }
    
    /// Searchable tags for cross-cutting concerns.
    public string[] Tags { get; init; } = [];
    
    /// DBRef of the player who created this page.
    public DBRef Author { get; init; }
    
    /// DBRef of the player who last edited this page.
    public DBRef LastEditor { get; init; }
    
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    
    /// Only admins can edit when locked.
    public bool Locked { get; init; }
    
    /// Hidden from non-admin users when false (draft state).
    public bool Published { get; init; } = true;
    
    /// Ordering within category listings.
    public int SortOrder { get; init; }
    
    /// Monotonically increasing version counter.
    public int Revision { get; init; }
    
    /// Optional: parent slug for hierarchical wiki structures.
    /// e.g., "rules/combat" has parent "rules"
    public string? ParentSlug { get; init; }
}
```

### WikiRevision Schema

```csharp
/// <summary>
/// A historical snapshot of a wiki page at a specific revision.
/// Used for diff/history/revert.
/// </summary>
public record WikiRevision
{
    public required string Slug { get; init; }
    public int Revision { get; init; }
    public required string Body { get; init; }       // Full body at this revision
    public required string Title { get; init; }      // Title at this revision
    public DBRef Editor { get; init; }
    public DateTime Timestamp { get; init; }
    public string? EditSummary { get; init; }        // "Fixed typo in combat rules"
}
```

### WikiCategory Schema (optional, for rich category metadata)

```csharp
/// <summary>
/// Optional category metadata — allows categories to have descriptions,
/// icons, and sort orders in listings.
/// </summary>
public record WikiCategory
{
    public required string Key { get; init; }         // "rules", "lore", etc.
    public required string DisplayName { get; init; } // "Game Rules"
    public string? Description { get; init; }
    public string? Icon { get; init; }                // MudBlazor icon name for web
    public int SortOrder { get; init; }
}
```

### Slug Conventions

Slugs support path-like hierarchies without requiring graph edges:

```
rules                     → top-level page
rules/combat              → child of "rules" (ParentSlug = "rules")
rules/combat/melee        → grandchild (ParentSlug = "rules/combat")
lore/places/tavern        → nested lore page
```

The slash in the slug is purely organizational. It enables:
- Breadcrumb navigation on the web (`Rules > Combat > Melee`)
- Category-like filtering (`wikilist(rules/*)` returns all pages under "rules")
- Tree-view rendering in the wiki index
- In-game: `wiki rules/combat` reads that specific page

## Markdown Extensions (SharpMUSH-specific)

Beyond standard CommonMark + GFM, these custom extensions are recognized:

### Wiki Links
```markdown
[[character-creation]]              → link to wiki page (uses page title as text)
[[character-creation|CharGen]]      → link with custom display text
[[#section-heading]]                → same-page anchor link
[[other-page#section]]              → cross-page anchor link
```

### Dynamic Includes (DEFERRED — Future Phase)

These are planned but will NOT be in the initial implementation:

```markdown
{{online-players}}                  → live count of connected players
{{recent-scenes:5}}                 → 5 most recent scene summaries
{{character:Gandalf:quote}}         → pulls the 'quote' attribute from character
{{object:#123:desc}}                → pulls desc attribute from object #123
{{toc}}                             → auto-generated table of contents
{{category:rules}}                  → lists all pages in 'rules' category
```

Rationale for deferral: Dynamic includes create render-time DB queries. They
complicate caching, create potential performance issues for popular pages, and
add complexity to the ANSI renderer. Ship static Markdown + wiki links first;
add dynamic includes once the base system is proven and performance characteristics
are understood.

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

## In-Game Interface: Functions as Primitives

### Design Philosophy

The wiki system is exposed primarily as **hardcode functions** — not as a monolithic
`+wiki` command. This follows the `textentries()` pattern: the engine provides the
primitives, and softcode authors build player-facing commands on top.

This means:
- A game focused on lore might create `+lore <topic>` → calls `wiki(lore/<topic>)`
- A game with detailed rules might create `+rules [section]` → calls `wikilist(rules/*)`
- A combat-focused game might wire `+help combat` → calls `wiki(help/combat)`
- Different games can present wiki content however they want via softcode

A minimal hardcode `wiki <slug>` command may exist as a default "I can always read
a wiki page" backstop, but the real power is in the function layer.

### Hardcode Functions

```
wiki(<slug>)                    → returns raw Markdown body of the page
wiki(<slug>, title)             → returns the page title
wiki(<slug>, category)          → returns the category string
wiki(<slug>, author)            → returns author DBRef
wiki(<slug>, lasteditor)        → returns last editor DBRef
wiki(<slug>, updated)           → returns last update timestamp (secs)
wiki(<slug>, created)           → returns creation timestamp (secs)
wiki(<slug>, revision)          → returns current revision number
wiki(<slug>, tags)              → returns space-separated tags
wiki(<slug>, exists)            → returns 1 if page exists, 0 otherwise
wiki(<slug>, locked)            → returns 1 if locked, 0 otherwise
wiki(<slug>, published)         → returns 1 if published, 0 otherwise

wikilist()                      → all published slugs, space-separated
wikilist(<category>)            → slugs in category, space-separated
wikilist(<prefix>/*)            → slugs matching prefix (wildcard)

wikisearch(<terms>)             → matching slugs (FTS), space-separated
wikisearch(<terms>, <limit>)    → with result limit

wikiset(<slug>, <field>, <value>)
                                → set a wiki field (permission-gated)
                                   fields: title, body, category, tags,
                                   locked, published, sortorder
                                → returns 1 on success, #-1 ERROR on failure

wikicreate(<slug>, <title>, <body>[, <category>])
                                → create a new wiki page
                                → returns slug on success, #-1 ERROR on failure

wikidelete(<slug>)              → delete a wiki page (admin only)
                                → returns 1 on success, #-1 ERROR on failure

wikirender(<slug>)              → returns ANSI-rendered content as MString
                                   (for embedding in descs, softcode output)

wikicategories()                → all category keys, space-separated

wikirevisions(<slug>)           → space-separated revision numbers
wikirevisions(<slug>, <rev>, body)  → body at a specific revision
wikirevisions(<slug>, <rev>, editor) → editor at a specific revision
```

### Minimal Hardcode Command (default backstop)

```
@wiki <slug>                    — displays the ANSI-rendered page
@wiki                           — shows brief usage and top categories
@wiki/list [category|prefix/*]  — lists pages (all, by category, by prefix)
@wiki/search <terms>            — full-text search, shows matching slugs + snippets
@wiki/set <slug>/<field>=<value>
                                — set a field on a page (permission-gated)
                                   fields: title, body, category, tags,
                                   locked, published, sortorder
@wiki/create <slug>=<title>     — create a new page (opens for body input or
                                   accepts @wiki/set <slug>/body=<content> after)
@wiki/delete <slug>             — delete a page (admin only, confirms)
@wiki/history <slug>            — show revision history
@wiki/revert <slug>=<rev>       — revert to a specific revision (admin only)
@wiki/categories                — list all categories with page counts
```

These mirror the function primitives but in an interactive admin-friendly form.
The `@` prefix signals a built-in engine command (like `@create`, `@set`, `@dig`).

Softcode commands built on the function layer (e.g., `+lore`, `+rules`) are the
player-facing UX. `@wiki` is the admin/builder tool for direct wiki management.

### Example Softcode Patterns

A game might install these as $commands on a master room object:

```mushcode
@@ +lore <topic> — reads from the lore/ category
&CMD_LORE Master=$+lore *:@assert haswiki(lore/%0)=
  {@pemit %#=No lore entry found for '%0'. Try: +lore/list};
  @pemit %#=wikirender(lore/%0)

@@ +lore/list — lists all lore pages
&CMD_LORE_LIST Master=$+lore/list:@pemit %#=
  [center(--- LORE ENTRIES ---,78,-)][iter(wikilist(lore),
  %r  [wiki(##,title)] %([ansi(c,##)]%),,%r)]

@@ +rules [section] — shows rules, or lists available sections
&CMD_RULES Master=$+rules *:@assert haswiki(rules/%0)=
  {@pemit %#=No rules section '%0'. Available: [wikilist(rules)]};
  @pemit %#=wikirender(rules/%0)

@@ +help <topic> — hybrid: tries wiki first, falls back to built-in help
&CMD_HELP Master=$+help *:@switch wiki(%0,exists)=
  1,{@pemit %#=wikirender(%0)},
  0,{@pemit %#=[textentries(help,%0)]}
```

### Permission Model for Functions

| Function        | Who can call it                                    |
|-----------------|----------------------------------------------------|
| wiki()          | Anyone (reads published pages; unpublished = admin)|
| wikilist()      | Anyone (returns only published unless admin)       |
| wikisearch()    | Anyone (searches only published unless admin)      |
| wikirender()    | Anyone (renders only published unless admin)       |
| wikiset()       | Builder+ for unlocked pages; Admin for locked      |
| wikicreate()    | Builder+ (or Player if wiki.player_edit_enabled)   |
| wikidelete()    | Admin only                                         |
| wikirevisions() | Any authenticated player                           |
| wikicategories()| Anyone                                             |

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

### No Automatic Redirect

The `help` command does NOT automatically redirect to wiki. They are separate systems:
- `help` → traditional help file entries (textentries-style)
- `wiki` → wiki pages (new system)

A game can choose to wire them together via softcode (see the `+help` example above),
but the engine does not assume this. The `wiki.help_redirect_to_wiki` config option
controls whether the minimal `wiki` hardcode command mentions help topics.

### Migration Path (Optional)

PennMUSH-style help files (`help.txt`, `news.txt`, `rules.txt`) can be imported
as a one-time migration. This is a tool, not an automatic behavior:

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

The existing `help` command remains unchanged. Games that want wiki-as-help can
implement it in softcode. The two systems coexist peacefully:

```
help combat             → reads from help.txt entries (textentries)
wiki rules/combat       → reads from wiki collection (new system)
```

Games have full freedom to unify, keep separate, or create hybrid approaches.

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
    
    [property: SharpConfig("Wiki Enabled", "Enable the wiki subsystem (functions + web endpoints)",
        Category = "Content", Group = "Wiki")]
    bool Enabled = true
);
```
