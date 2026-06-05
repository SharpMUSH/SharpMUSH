# Area 14: Search Infrastructure — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (14.1–14.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Indexing
- [ ] Set up ArangoSearch view (or SurrealDB FTS indexes) on wiki, characters, scenes, help
- [ ] Configure text analyzer (stemming, lowercase, accent-fold)
- [ ] Wiki: extract plain text on save → store in text_plain field
- [ ] Profiles: extract searchable text (public fields only) on save
- [ ] Scenes: extract pose plain text on scene completion
- [ ] Help files: extract plain text on save
- [ ] Verify indexes update automatically on document write

### Search Service
- [ ] `ISearchService` interface (query, type filter, role, character_id)
- [ ] AQL query builder with permission filtering (role + character bind params)
- [ ] Result model: type, title, snippet (highlighted), url, relevance score
- [ ] Grouping: results grouped by type with per-type count
- [ ] Pagination within groups (default 10 per type on full page)

### Web UI
- [ ] Search bar component in TopBar (always visible)
- [ ] Debounced input (300ms) → instant suggestions dropdown
- [ ] Suggestions: max 2-3 per type, "See all" link
- [ ] Full results page (`/search?q=...`)
- [ ] Type filter dropdown (All, Wiki, Characters, Scenes, Help)
- [ ] Snippet highlighting (match terms wrapped in <mark>)
- [ ] Empty state / no results messaging

### Configuration
- [ ] `search.debounce_ms`: 300
- [ ] `search.suggestions_per_type`: 3
- [ ] `search.results_per_type`: 10
- [ ] `search.min_query_length`: 2
- [ ] `search.highlight_tag`: "mark"

## Testing
- [ ] Index updates: save wiki page → immediately searchable
- [ ] Permission filtering: guest can't see protected wiki in results
- [ ] Participant-only scenes don't appear for non-participants
- [ ] Omnisearch: type correctly, get grouped results
- [ ] Empty query / too-short query handled gracefully
- [ ] Highlight: search terms marked in snippets
