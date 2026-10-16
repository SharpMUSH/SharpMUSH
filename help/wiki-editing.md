<!-- help-article
{
  "corpus": "help",
  "id": "wiki-editing",
  "lookup": "wiki editing",
  "aliases": ["WIKI-EDITING", "@WIKI/CREATE", "@WIKI/EDIT", "@WIKI/APPEND"],
  "sections": [
    {"id": "creating", "heading": "Creating and editing", "lookup": "wiki creating"},
    {"id": "rollback", "heading": "Rolling back", "lookup": "wiki rollback", "aliases": ["@WIKI/ROLLBACK"]},
    {"id": "translating", "heading": "Translating pages", "lookup": "wiki translating", "aliases": ["@WIKI/TRANSLATE"]},
    {"id": "example", "heading": "Example", "lookup": "wiki example"}
  ],
  "redirects": {"wiki authoring commands": "wiki editing"}
}
-->
# Wiki Editing

- `@wiki/create <title>=<markdown>`
- `@wiki/edit <page>=<markdown>`
- `@wiki/append <page>=<markdown>`
- `@wiki/rollback <page>=<revision #>`

Creating a page needs `wiki.create`, and editing one `wiki.edit`, along with
whatever the page's namespace, categories and the page itself require (see
[wiki permissions]); translations count as edits. A character's own biography
is no exception: writing it needs `wiki.create`, and editing it or changing its
gallery on the portal needs `wiki.edit`, as for any other page. A new game gives both permissions to the
`approved` role and, through `wiki.admin`, to staff (see [roles]). Each page
records its author and last editor by dbref.

- [wiki creating]: creating, replacing and appending to pages
- [wiki rollback]: restoring an earlier revision
- [wiki translating]: writing a page's translation
- [wiki example]: a short session

## Creating and editing

@wiki/create makes a new wiki page. The title may carry a namespace prefix
(`@wiki/create Help:House Rules=# House Rules`); the page's URL slug is
derived from the title (lower-case, spaces become underscores).

@wiki/edit replaces a page's entire Markdown body. @wiki/append adds the given
Markdown as a new paragraph at the end, handy for building up a page from a
telnet client one block at a time. Every edit records a revision; see
[wiki administration].

To change a page's existing text, read its source with `@wiki/md` first (see
[wiki reading source]).

## Rolling back

@wiki/rollback restores the page body from an earlier revision (find the
number with `@wiki/history`; see [wiki administration]). The restore is a normal edit: it creates a NEW
revision rather than rewriting history, so a rollback can itself be rolled
back. The web portal offers the same action via the Restore button in each
page's history dialog.

## Translating pages

- `@wiki/translate <page>/<lang>=<markdown>`

@wiki/translate writes one locale's translation of a page: the same rows the
web portal's language selector edits. The language is part of the target,
after a slash: `@wiki/translate combat_primer/fr=...`. It is required, and it
is never taken from your `LOCALE`; without it (or with a language tag SharpMUSH
does not recognise) the command refuses and writes nothing. `@wiki/edit` writes
the page itself, so translating a page into the language it was written in is
refused too.

A translation keeps its own title, revision numbers and draft flag; the page's
categories and requirements are inherited and cannot differ. @wiki/translate
supplies only the body, so an existing translation keeps the title and the
draft/published state it already had, and a brand-new one starts published,
under the source page's title. Retitle it on the web portal.

If somebody else saves the same translation between your reading it and your
sending the command, you are told so and *nothing is written*; re-read the
page and re-apply your text. The command never retries by itself, because a
retry would put your older text on top of theirs.

::: seealso
- [wiki]
- [wiki administration]
- [wiki locale selection]
:::

## Example

```sharp
> @wiki/create Combat Primer=# Combat Primer
WIKI: Created page 'Combat Primer' (combat_primer).
> @wiki/append combat_primer=Roll initiative with `+init`.
WIKI: Appended to 'Combat Primer' (now rev 2).
> @wiki/translate combat_primer/fr=# Manuel de Combat
WIKI: Wrote the fr translation of 'Combat Primer' (now rev 1).
> @wiki/translate combat_primer=# Manuel de Combat
WIKI: a translation needs an explicit language: @wiki/translate <page>/<lang>=<text>
```

::: seealso
- [wiki editing]
:::
