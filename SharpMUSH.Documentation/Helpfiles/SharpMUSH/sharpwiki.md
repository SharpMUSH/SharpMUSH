# WIKI
# @WIKI

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
* `@wiki/search <text>` - find pages by title or content
* `@wiki/recent [<count>]` - recently edited pages (default 10)
* `@wiki/history <page>` - revision history

Authoring:
* `@wiki/create <title>=<markdown>` - create a page
* `@wiki/edit <page>=<markdown>` - replace a page's content
* `@wiki/append <page>=<markdown>` - add a paragraph to a page

Administration:
* `@wiki/delete <page>` - delete a page (wizard)
* `@wiki/protect <page>`, `@wiki/unprotect <page>` - restrict edits to wizards (wizard)
* `@wiki/publish <page>`, `@wiki/unpublish <page>` - publish or mark as draft (wizard)
* `@wiki/category <page>=<category>` - set or clear the page's category
* `@wiki/tag <page>=<tag> <tag> ...` - replace the page's tags

The `/noeval` switch may be combined with any of the above to suppress
softcode evaluation of the arguments.

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

- `@wiki/create <title>=<markdown>`
- `@wiki/edit <page>=<markdown>`
- `@wiki/append <page>=<markdown>`

@wiki/create makes a new wiki page. The title may carry a namespace prefix
(`@wiki/create Help:House Rules=# House Rules`); the page's URL slug is
derived from the title (lower-case, spaces become underscores).

@wiki/edit replaces a page's entire Markdown body. @wiki/append adds the given
Markdown as a new paragraph at the end — handy for building up a page from a
telnet client one block at a time. Every edit records a revision; see
[@wiki/history].

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

@wiki/history lists every revision with its editor, date, and edit summary.

**See Also:**
- [@wiki]
- [wiki-editing]

# WIKI()

- `wiki(<page>)`
- `wiki(<page>, <field>)`

Returns information about a wiki page. With one argument, returns the page's
plain-text content. The page target accepts a namespace prefix
(`wiki(help:markdown_guide)`).

The optional second argument selects a field:
* `text` - plain text content (the default)
* `markdown` - the raw Markdown source
* `title` - the display title
* `category` - the category, or an empty string
* `tags` - space-separated tag list
* `namespace` - main, help, character, or system
* `revision` - the current revision number
* `updated` - the last-edit time as a Unix timestamp (secs)
* `author` - the dbref of the page's creator

Returns #-1 NO SUCH WIKI PAGE when the page does not exist.

### Example
```sharp
> think wiki(home, title)
Home
> think wiki(help:markdown_guide, revision)
1
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
contains *<text>* (case-insensitive). Limited to the first 100 matches.

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
