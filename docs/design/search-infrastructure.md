# Search Infrastructure

## Overview

Omnisearch powered by the graph DB's native full-text search. Single search
bar in top nav. Results grouped by type. Permission-filtered at query time.

## Backend

### ArangoDB Path (Primary)

ArangoSearch with custom analyzers:

```
View: portal_search_view
  Linked collections:
    - wiki_pages (fields: title, text_plain, namespace, tags)
    - characters (fields: name, profile_summary, public_fields)
    - scene_archives (fields: title, text_plain, participant_names)
    - help_files (fields: title, text_plain, category)

Analyzer: text_en
  - tokenize (stem)
  - normalize (lowercase, accent-fold)
  - n-grams for prefix matching (autocomplete)
```

### SurrealDB Path (Alternative)

SurrealDB full-text indexes on the same fields. Same query patterns,
different syntax.

### No External Engine (v1)

No Elasticsearch, no Meilisearch. The DB's native FTS is sufficient for
the expected scale (hundreds to low thousands of documents, not millions).
If scale demands it later, Meilisearch can be added as a sidecar — the
indexing pipeline (plain text extraction on write) already produces the
right data shape.

## Indexing Pipeline

Content is indexed on write. No background reindex jobs needed for normal
operation.

```
Wiki page saved
  → Markdig → strip to plain text
  → Store in text_plain field
  → ArangoSearch auto-indexes

Profile field saved
  → MString → .ToPlainText() or Markdig → plain text (per format)
  → Store in searchable_text field
  → ArangoSearch auto-indexes

Scene completed
  → All poses: MString → .ToPlainText()
  → Concatenate (or store per-pose with scene_id)
  → Store in text_plain field
  → ArangoSearch auto-indexes

Help file written
  → Markdig → plain text
  → Store in text_plain field
  → ArangoSearch auto-indexes
```

## Omnisearch UI

### Search Bar (Top Nav)

- Always visible in TopBar
- Placeholder: "Search wiki, characters, scenes..."
- Debounced input (300ms) → instant suggestions dropdown
- Enter → full results page

### Instant Suggestions (Dropdown)

```
┌──────────────────────────────────────┐
│  🔍 "dragon"                         │
├──────────────────────────────────────┤
│  Wiki                                │
│    Dragon Lore                       │
│    Dragon Riders                     │
│                                      │
│  Characters                          │
│    Dragonbane                        │
│                                      │
│  Scenes                              │
│    The Dragon's Lair (archived)      │
│                                      │
│  [See all results →]                 │
└──────────────────────────────────────┘
```

- Max 2-3 results per type in dropdown
- Click result → navigate directly
- Click "See all" → full results page with that query

### Full Results Page (`/search?q=dragon`)

```
┌─────────────────────────────────────────────────────────┐
│  Results for "dragon"                    [Filter ▾]      │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  📄 Wiki (4 results)                                    │
│    Dragon Lore — "...the ancient dragons of the..."     │
│    Dragon Riders — "...bonded pairs who ride..."        │
│    Magic System — "...dragon fire is a form of..."      │
│    History — "...the dragon wars of the Third Age..."   │
│                                                         │
│  👤 Characters (2 results)                              │
│    Dragonbane — "A scarred dragonslayer from..."        │
│    Ember — "Half-dragon sorceress..."                   │
│                                                         │
│  🎭 Scenes (1 result)                                   │
│    The Dragon's Lair — "3 participants, completed..."   │
│                                                         │
│  📖 Help (1 result)                                     │
│    Combat System — "...dragon-type enemies have..."     │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

- Grouped by type with count
- Snippet shows context around match (highlighted)
- Filter dropdown: All, Wiki only, Characters only, Scenes only, Help only
- Results paginated within each group (default: 10 per type on full page)

## Permission Filtering

Search results are filtered BEFORE return. Users never see content they
can't access.

```csharp
// Pseudo-code for search query
var results = await SearchIndex.Query(query);

results = results.Where(r => r.Type switch
{
    "wiki" => r.Visibility == "public"
              || (r.Visibility == "protected" && role >= Player)
              || role >= Royalty,
    "character" => r.IsPublic || role >= Royalty,
    "scene" => r.Visibility == "public"
               || r.Participants.Contains(currentCharId)
               || role >= Royalty,
    "help" => true,  // help is always public
    _ => false
});
```

**ArangoDB implementation:** Filter in AQL query (not post-filter). Pass role
and character ID as bind parameters. This keeps result counts accurate and
pagination correct.

## Search Configuration

```yaml
search:
  debounce_ms: 300              # Autocomplete delay
  suggestions_per_type: 3       # Dropdown results per category
  results_per_type: 10          # Full page results per category
  min_query_length: 2           # Don't search for single characters
  highlight_tag: "mark"         # HTML tag for match highlighting
```
