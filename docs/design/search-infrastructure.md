# Search Infrastructure

## Overview

Omnisearch is a provider-neutral service surfaced through a single top-navigation search box.
Results are grouped by wiki page, character, scene, and help entry and are permission-filtered
before pagination.

## Storage strategy

- SurrealDB may use native full-text indexes over normalized plain-text fields.
- Lightning maintains the same searchable projection in its embedded store and evaluates the
  provider-neutral query contract locally.
- No external search sidecar is required for the expected installation size.

Content is normalized on write: Markdown and `MString` values are converted to plain text,
then stored with the source record's identity, type, visibility, and ordering metadata. A
rebuild operation can recreate the projection from authoritative records.

## Permission filtering

The search service applies visibility constraints before it calculates counts or pages:

- published wiki pages are public; drafts require the normal wiki permission;
- public character fields are searchable, while private fields require elevated permission;
- private scenes are visible only to participants and staff;
- help entries are public.

Each provider must implement these constraints inside its query path. Post-filtering would
produce incorrect counts and could disclose the presence of private records.

## UI contract

- Input is debounced and ignores queries shorter than two characters.
- Suggestions return at most three results per category.
- The full page returns ten results per category and includes contextual snippets.
- Search text is encoded as data and never interpreted as markup.
