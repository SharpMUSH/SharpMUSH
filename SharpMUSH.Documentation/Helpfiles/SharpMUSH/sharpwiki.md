# WIKI
# @WIKI
# @WIKI/SOURCE

- `@wiki <page>`
- `@wiki/<switch> <page>[=<value>]`

@wiki is the in-game interface to the shared wiki. Wiki pages live in the same
database the web portal serves, so anything you create or edit in-game appears
on the website immediately, and vice versa.

Page targets may carry a namespace prefix: `Help:Markdown Guide` refers to the
page "Markdown Guide" in the help namespace. Without a prefix, pages live in
the main namespace. Valid namespaces: main, help, character, system.

Viewing and discovery:
* `@wiki <page>` or `@wiki/view <page>` - display a page
* `@wiki/list [<namespace>]` - list pages
* `@wiki/search <text>` - find pages by title or content, in any locale
* `@wiki/recent [<count>]` - recently edited pages (default 10)
* `@wiki/history <page>` - revision history

Authoring:
* `@wiki/create <title>=<markdown>` - create a page
* `@wiki/edit <page>=<markdown>` - replace a page's content
* `@wiki/append <page>=<markdown>` - add a paragraph to a page
* `@wiki/rollback <page>=<revision #>` - restore an earlier revision

Administration:
* `@wiki/delete <page>` - delete a page (wizard)
* `@wiki/protect <page>`, `@wiki/unprotect <page>` - restrict edits to wizards (wizard)
* `@wiki/publish <page>`, `@wiki/unpublish <page>` - publish or mark as draft (wizard)
* `@wiki/category <page>=<category>` - set or clear the page's category
* `@wiki/tag <page>=<tag> <tag> ...` - replace the page's tags

The `/noeval` switch may be combined with any of the above to suppress
softcode evaluation of the arguments.

Locale:
@wiki reads pages in your locale, the one you set with `@locale`. When a page
has no translation in your locale you get the fallback version, and its locale
appears in brackets next to the revision number on the header line.

* `@wiki/view/source <page>` - read the page in the locale it was written in,
  ignoring yours. Useful when translating.
* `@wiki/history <page>` shows your locale's revision stream, which is numbered
  separately from the source's; `@wiki/history/source <page>` shows the source
  locale's.

Like `/noeval`, `/source` is a modifier rather than an action, so it combines
with `/view`, `/history` and `/search` instead of replacing them.

`@wiki/search` matches every locale a page has been translated into, not just
the one it was written in, so you find a page by whatever wording you remember.
Each page is listed once however many of its locales matched; when the text
that matched was not the page's own source locale, that locale appears in
brackets after the line, and if several locales matched, yours is the one
shown. `@wiki/search/source <text>` matches source text only.

Drafts stay out of the way: unpublished pages and unpublished translations are
invisible to anyone but a wizard in `@wiki/list`, `@wiki/search` and
`@wiki/recent`, and are never returned by `wikilist()`, `wikisearch()` or
`wikirecent()`.

Page content is Markdown; see `help markdown` or the wiki's own
"Help:Markdown Guide" page (`@wiki help:markdown_guide`) for the supported
syntax. Live listing blocks (`::: category ...`) render on the web portal and
appear in-game as a placeholder.

**See Also:**
- [wiki-editing]
- [wiki-admin]
- [wiki()]

# WIKI-EDITING
# @WIKI/CREATE
# @WIKI/EDIT
# @WIKI/APPEND
# @WIKI/ROLLBACK

- `@wiki/create <title>=<markdown>`
- `@wiki/edit <page>=<markdown>`
- `@wiki/append <page>=<markdown>`
- `@wiki/rollback <page>=<revision #>`

@wiki/create makes a new wiki page. The title may carry a namespace prefix
(`@wiki/create Help:House Rules=# House Rules`); the page's URL slug is
derived from the title (lower-case, spaces become underscores).

@wiki/edit replaces a page's entire Markdown body. @wiki/append adds the given
Markdown as a new paragraph at the end — handy for building up a page from a
telnet client one block at a time. Every edit records a revision; see
[@wiki/history].

@wiki/rollback restores the page body from an earlier revision (find the
number with [@wiki/history]). The restore is a normal edit: it creates a NEW
revision rather than rewriting history, so a rollback can itself be rolled
back. The web portal offers the same action via the Restore button in each
page's history dialog.

Protected pages can only be edited by wizards. Each page records its author
and last editor by dbref.

### Example
```sharp
> @wiki/create Combat Primer=# Combat Primer
WIKI: Created page 'Combat Primer' (combat_primer).
> @wiki/append combat_primer=Roll initiative with `+init`.
WIKI: Appended to 'Combat Primer' (now rev 2).
```

**See Also:**
- [@wiki]
- [wiki-admin]

# WIKI-ADMIN
# @WIKI/DELETE
# @WIKI/PROTECT
# @WIKI/UNPROTECT
# @WIKI/PUBLISH
# @WIKI/UNPUBLISH
# @WIKI/CATEGORY
# @WIKI/TAG
# @WIKI/HISTORY

- `@wiki/delete <page>`
- `@wiki/protect <page>` and `@wiki/unprotect <page>`
- `@wiki/publish <page>` and `@wiki/unpublish <page>`
- `@wiki/category <page>=<category>`
- `@wiki/tag <page>=<tag> <tag> ...`
- `@wiki/history <page>`

Deleting, protecting, and publishing are wizard-only. Deletion removes the
page and its entire revision history. Protected pages refuse edits from
non-wizards both in-game and on the web portal. Unpublished pages are drafts:
hidden from anonymous web visitors and from the sitemap, but still visible to
logged-in users and in-game.

Categories and tags group pages for the web portal's listings and the wiki's
live `::: category` blocks. Tags are space-separated; both are stored
lower-case. Setting an empty category clears it.

@wiki/history lists every revision with its editor, date, and edit summary, for
the revision stream of your own locale. Each locale is numbered independently
starting from 1, so "rev 3" of a French translation is unrelated to "rev 3" of
the source; `@wiki/history/source` shows the source locale's stream.

**See Also:**
- [@wiki]
- [wiki-editing]

# WIKI()

- `wiki(<page>)`
- `wiki(<page>, <field>)`
- `wiki(<page>, <field>, <locale>)`

Returns information about a wiki page. With one argument, returns the page's
plain-text content. The page target accepts a namespace prefix
(`wiki(help:markdown_guide)`).

The optional second argument selects a field:
* `text` - plain text content (the default)
* `markdown` - the raw Markdown source
* `title` - the display title
* `locale` - the locale actually served (see below)
* `category` - the category, or an empty string
* `tags` - space-separated tag list
* `namespace` - main, help, character, or system
* `revision` - the current revision number in the served locale
* `updated` - the last-edit time as a Unix timestamp (secs)
* `author` - the dbref of the page's creator

The optional third argument names a locale; it defaults to your `LOCALE`.
`text`, `markdown`, `title`, `revision` and `locale` come from the translation
in that locale; everything else is page metadata and is the same in every
locale. When there is no translation you get the fallback version rather than
an error, and the `locale` field is how softcode detects that: it returns the
locale that was served, not the one you asked for. An unparseable locale is
treated as if you had passed none.

Unpublished translations are never reachable from `wiki()`.

Returns #-1 NO SUCH WIKI PAGE when the page does not exist.

### Example
```sharp
> think wiki(home, title)
Home
> think wiki(help:markdown_guide, revision)
1
> think wiki(markdown_guide, locale, fr)
en
```

**See Also:**
- [wikilist()]
- [wikisearch()]
- [wikirecent()]

# WIKILIST()

- `wikilist()`
- `wikilist(<namespace>)`

Returns a space-separated list of wiki page references, optionally restricted
to one namespace. Main-namespace pages appear as their slug; other pages as
`namespace:slug` — both forms are valid inputs to [wiki()] and `@wiki`.

A page reference is its canonical slug, which does not change between locales,
so this list is the same whatever your `LOCALE` is. Pass a reference from it to
[wiki()] with a locale to read that page's translation.

### Example
```sharp
> think wikilist(help)
help:markdown_guide
```

**See Also:**
- [wiki()]
- [wikisearch()]

# WIKISEARCH()

- `wikisearch(<text>)`

Returns a space-separated list of page references whose title or content
contains *<text>* (case-insensitive), in any locale the page has been
translated into. Each page appears once however many of its locales matched.
Unpublished pages and unpublished translations are never returned. Limited to
the first 100 matches.

### Example
```sharp
> think wikisearch(combat)
combat_primer house_rules
```

**See Also:**
- [wiki()]
- [wikilist()]

# WIKIRECENT()

- `wikirecent()`
- `wikirecent(<count>)`

Returns a space-separated list of the most recently edited page references,
newest first. *<count>* defaults to 10 and is clamped to 1-50.

**See Also:**
- [wiki()]
- [wikilist()]
