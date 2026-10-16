<!-- help-article
{
  "corpus": "help",
  "id": "wiki",
  "lookup": "wiki",
  "aliases": ["@WIKI", "@WIKI/SOURCE", "@WIKI/DRAFT"],
  "sections": [
    {"id": "viewing-and-discovery", "heading": "Viewing and discovery", "lookup": "wiki viewing and discovery", "aliases": ["@WIKI/VIEW", "@WIKI/LIST", "@WIKI/RECENT"]},
    {"id": "search", "heading": "Searching", "lookup": "wiki search", "aliases": ["@WIKI/SEARCH"]},
    {"id": "reading-source", "heading": "Reading source", "lookup": "wiki reading source", "aliases": ["@WIKI/MD"]},
    {"id": "reading-drafts", "heading": "Reading drafts", "lookup": "wiki reading drafts"},
    {"id": "drafts-in-listings", "heading": "Drafts in listings", "lookup": "wiki drafts in listings"},
    {"id": "locale-selection", "heading": "Translations and locales", "lookup": "wiki locale selection"},
    {"id": "modifiers", "heading": "Modifiers", "lookup": "wiki modifiers"},
    {"id": "linking", "heading": "Linking pages", "lookup": "wiki links"}
  ]
}
-->
# Wiki

- `@wiki <page>`
- `@wiki/<switch> <page>[=<value>]`

@wiki is the in-game interface to the shared wiki. Wiki pages live in the same
database the web portal serves, so anything you create or edit in-game appears
on the website immediately, and vice versa.

Page targets may carry a namespace prefix: `Help:Markdown Guide` refers to the
page "Markdown Guide" in the help namespace. Without a prefix, pages live in
the main namespace. Valid namespaces: main, help, character, system, category.

Page content is Markdown; see [rendermarkdown supported markdown features], or
the wiki's own "Help:Markdown Guide" page (`@wiki help:markdown_guide`). The
`/noeval` switch may be combined with any `@wiki` switch to suppress softcode
evaluation of the arguments.

- [wiki viewing and discovery]: reading and listing pages
- [wiki search]: searching every locale, and what is hidden
- [wiki reading source]: a page's Markdown, exactly as stored
- [wiki reading drafts]: unpublished pages and who may read them
- [wiki drafts in listings]: how drafts stay out of discovery
- [wiki locale selection]: your locale, and reading translations
- [wiki modifiers]: the `/source`, `/md` and `/draft` modifiers
- [wiki links]: linking one page to another
- [wiki editing]: creating, editing, translating, rolling back
- [wiki administration]: deleting, publishing, permissions
- [wiki categories]: filing pages in categories, and pinning

::: seealso
- [WIKI()]
:::

## Viewing and discovery

- `@wiki <page>` or `@wiki/view <page>` - display a page
- `@wiki/list [<namespace>]` - list pages
- `@wiki/category <name>` - list the pages and subcategories in a category
- `@wiki/search <text>` - find pages by title or content, in any locale
- `@wiki/search/source <text>` - match source text only
- `@wiki/recent [<count>]` - recently edited pages (default 10)
- `@wiki/history <page>` - revision history
- `@wiki/md <page>` - show the page's markdown source instead of the rendered body
- `@wiki/view/draft <page>` - also render the page if it is a draft (see [wiki reading drafts])

Listings (`@wiki/list`, `@wiki/search`, `@wiki/recent`) print every page's
identifier fully qualified as `<namespace>:<slug>`; `home` in the main
namespace lists as `main:home`. One column, one grammar, and the identifier
printed is always one you can paste straight back into `@wiki`. Typing a
target is unchanged: the short form still works, so `@wiki home` and
`@wiki main:home` reach the same page.

## Searching

`@wiki/search` matches every locale a page has been translated into, not just
the one it was written in, so you find a page by whatever wording you remember.
Each page is listed once however many of its locales matched; when the text
that matched was not the page's own source locale, that locale appears in
brackets after the line, and if several locales matched, yours is the one
shown. `@wiki/search/source <text>` matches source text only. `*` and `?` in
`<text>` are wildcards, matched anywhere in a title or body.

Not every page is listed for everybody. Drafts are left out for anyone who may
not read them (see [wiki reading drafts]). A page you may not read because its
namespace, a category it is in, or the page itself requires a permission you
lack (see [wiki permissions]) is left out of every listing and answers as a
page that does not exist.

::: seealso
- [WIKILIST()]
- [WIKISEARCH()]
- [WIKIRECENT()]
:::

## Reading source

`@wiki/md <page>` prints the markdown a page is stored with, exactly as stored -
nothing is rendered, reflowed or wrapped, and the page's own `[`, `%` and `$`
reach you as text. That is the copy you edit: read it with `/md`, change it, and
put it back with `@wiki/edit <page>=<markdown>`. `@wiki/view/source/md <page>`
gives a translator the source locale's markdown to work from before writing
`@wiki/translate`. The softcode equivalent is `wiki(<page>, markdown)`.

`/md` grants nothing: a draft still needs `/draft` and still needs
`wiki.drafts`, so asking for a draft's source is still asking for a draft.

## Reading drafts

- `@wiki/view/draft <page>` - render the draft's body as well
- `@wiki/history/draft <page>` - list the draft's revisions as well

An unpublished page is a draft, and `@wiki` does not render a draft's body or
its revision history to anybody by default. You are told the page is a draft
rather than told it does not exist; the header still names it and marks it
`(draft)`.

`/draft` is an opt-in, not a permission: only the draft's author and holders of
`wiki.drafts` can read a draft, and for everybody else the answer is exactly the same with the switch as without
it, so `/draft` can never be used to find out whether a draft exists. Even a
`wiki.drafts` holder reads a draft's body or history only by adding `/draft`.
Being able to *edit* a page is not enough: that is `wiki.edit`, and seeing
other people's drafts is `wiki.drafts`.

The same rule covers an unpublished *translation*: you get the published
version in whatever language the page does have, not a withheld French draft.
A page that is itself unpublished stays withheld however its individual
translations are flagged, so publishing one language does not publish the
article.

## Drafts in listings

Unpublished pages and unpublished translations are shown only to their author
and to holders of `wiki.drafts`; for everybody else `@wiki/list`,
`@wiki/search` and `@wiki/recent` omit them, and `wiki()`, `wikilist()`,
`wikisearch()` and `wikirecent()` never return them to anybody. `@wiki/list`'s
totals count only what you are allowed to see, so the count does not give a
draft away either. `/draft` has no effect on `/search`, which already includes
drafts for anyone who can see them.

That keeps a draft's title, text and existence out of discovery. It is still not
a secret store: asking for a page by name tells you whether one is there. Put
nothing in a draft that the people who can guess its name should not know exists.

On the web portal, drafts are hidden from anonymous visitors and from the
sitemap. To publish or unpublish a page, see [wiki administration].

## Translations and locales

- `@wiki/view/source <page>` - read the page in the locale it was written in, ignoring yours; useful when translating
- `@wiki/history <page>` - your locale's revision stream, numbered separately from the source's
- `@wiki/history/source <page>` - the source locale's revision stream
- `@wiki/translate <page>/<lang>=<markdown>` - write the `<lang>` translation (see [wiki editing])

@wiki reads pages in your locale, the one you set with `@locale` (see
[@locale]). When a page has no translation in your locale you get the fallback
version, and its locale appears in brackets next to the revision number on the
header line.

Your `LOCALE` decides what you *read*. It never decides what you *write*:
`@wiki/translate` takes the language in the command and refuses to run without
it. Getting a read wrong shows you the wrong translation, which you can see and
undo; getting a write wrong files your English under French, which you cannot.

## Modifiers

`/noeval`, `/source`, `/md` and `/draft` are modifiers rather than actions, so
they combine with `/view` and `/history` instead of replacing them. They are
orthogonal and stack:

- `/source` chooses the locale
- `/md` chooses raw over rendered (it has no effect on `/history` or the listings)
- `/draft` decides whether an unpublished body is shown at all (it has no effect on `/search`)

## Linking pages

- `[[Page Name]]`
- `[[Display text|Page Name]]`

Inside a page's Markdown, `[[Page Name]]` links to another wiki page. The
target takes the same forms `@wiki` does, so `[[Home]]` and
`[[Help:Markdown Guide]]` reach the pages you would reach by typing them.
`[[Display text|Page Name]]` links with wording of your own; without it the
page's title is shown. Only the display text appears - the brackets and the
target never do.

On the web portal that is an ordinary link. In-game, `@wiki` renders it as a
clickable command link that runs `@wiki <namespace>:<slug>` for the
page named, so a Pueblo, MXP or web client follows a wiki link by clicking it.
A plain telnet client cannot click, and sees the underlined text it always saw.

The link is clickable under `@wiki` and nowhere else. The same page text
through `wiki()`, or any Markdown you render yourself with `rendermarkdown()`
or `rendermarkdowncustom()`, gives you the underlined display text and nothing
attached to it - softcode that renders a page is free to present its links
however it likes, and `@wiki` is the only surface that already knows how to
follow one.

`[[Category:Lore]]` is an ordinary link to a category's page; it does not file
the page in that category (see [wiki categories]).

::: seealso
- [WIKI()]
- [RENDERMARKDOWN()]
- [RENDERMARKDOWNCUSTOM()]
:::
