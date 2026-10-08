<!-- help-article
{
  "corpus": "help",
  "id": "wiki",
  "lookup": "wiki",
  "aliases": [
    "@WIKI",
    "@WIKI/SOURCE",
    "@WIKI/DRAFT"
  ],
  "sections": [
    {
      "id": "viewing-and-discovery",
      "heading": "Viewing and discovery",
      "lookup": "wiki viewing and discovery"
    },
    {
      "id": "reading-source",
      "heading": "Reading source",
      "lookup": "wiki reading source"
    },
    {
      "id": "reading-drafts",
      "heading": "Reading drafts",
      "lookup": "wiki reading drafts"
    },
    {
      "id": "locale-selection",
      "heading": "Translations and locales",
      "lookup": "wiki locale selection"
    },
    {
      "id": "linking",
      "heading": "Linking pages",
      "lookup": "wiki links"
    },
    {
      "id": "authoring-commands",
      "heading": "Authoring commands",
      "lookup": "wiki authoring commands"
    },
    {
      "id": "editing",
      "heading": "Editing wiki pages",
      "lookup": "wiki editing",
      "aliases": [
        "WIKI-EDITING",
        "@WIKI/CREATE",
        "@WIKI/EDIT",
        "@WIKI/APPEND",
        "@WIKI/ROLLBACK",
        "@WIKI/TRANSLATE"
      ]
    },
    {
      "id": "example",
      "heading": "Example",
      "lookup": "wiki example"
    },
    {
      "id": "administration-commands",
      "heading": "Administration commands",
      "lookup": "wiki administration commands"
    },
    {
      "id": "administration",
      "heading": "Wiki administration",
      "lookup": "wiki administration",
      "aliases": [
        "WIKI-ADMIN",
        "@WIKI/DELETE",
        "@WIKI/PROTECT",
        "@WIKI/UNPROTECT",
        "@WIKI/PUBLISH",
        "@WIKI/UNPUBLISH",
        "@WIKI/HISTORY"
      ]
    },
    {
      "id": "permissions",
      "heading": "Wiki permissions",
      "lookup": "wiki permissions",
      "aliases": [
        "WIKI-PERMISSIONS",
        "@WIKI/REQUIRE",
        "@WIKI/ACCESS"
      ]
    },
    {
      "id": "categories",
      "heading": "Wiki categories",
      "lookup": "wiki categories",
      "aliases": [
        "WIKI-CATEGORIES",
        "@WIKI/CATEGORY",
        "@WIKI/PIN",
        "@WIKI/UNPIN"
      ]
    }
  ],
  "redirects": {}
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
A page is identified by its namespace and title alone; categories are labels
it carries (see [wiki categories]).

The `/noeval` switch may be combined with any `@wiki` switch to suppress
softcode evaluation of the arguments.

Page content is Markdown; see [rendermarkdown supported markdown features], or
the wiki's own "Help:Markdown Guide" page (`@wiki help:markdown_guide`). Live listing blocks
(`::: category ...`) render on the web portal and appear in-game as a
placeholder.

The rest of this topic:

- [wiki viewing and discovery] - reading, listing and searching pages
- [wiki reading source] - a page's Markdown, exactly as stored
- [wiki reading drafts] - unpublished pages and who may read them
- [wiki locale selection] - translations, your locale, and the `/source`, `/md` and `/draft` modifiers
- [wiki links] - linking one page to another
- [wiki authoring commands] and [wiki editing] - creating, editing and translating pages
- [wiki example] - a short session
- [wiki administration commands] and [wiki administration] - deleting, protecting, publishing, history
- [wiki permissions] - what a namespace, category or page requires
- [wiki categories] - filing pages in categories

::: seealso
- [wiki editing]
- [wiki administration]
- [wiki categories]
- [wiki permissions]
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

### Drafts in listings

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

### Modifiers

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

## Authoring commands

- `@wiki/create <title>=<markdown>` - create a page
- `@wiki/edit <page>=<markdown>` - replace a page's content
- `@wiki/append <page>=<markdown>` - add a paragraph to a page
- `@wiki/rollback <page>=<revision #>` - restore an earlier revision
- `@wiki/translate <page>/<lang>=<markdown>` - write one locale's translation
- `@wiki/category <page>=<names>` - set a page's categories (comma-separated; see [wiki categories])

Each is described in [wiki editing].

## Editing wiki pages

- `@wiki/create <title>=<markdown>`
- `@wiki/edit <page>=<markdown>`
- `@wiki/append <page>=<markdown>`
- `@wiki/rollback <page>=<revision #>`
- `@wiki/translate <page>/<lang>=<markdown>`

Creating a page needs `wiki.create`, and editing one `wiki.edit`, along with
whatever the page's namespace, categories and the page itself require (see
[wiki permissions]); translations count as edits. Each page records its author
and last editor by dbref.

### Creating and editing

@wiki/create makes a new wiki page. The title may carry a namespace prefix
(`@wiki/create Help:House Rules=# House Rules`); the page's URL slug is
derived from the title (lower-case, spaces become underscores).

@wiki/edit replaces a page's entire Markdown body. @wiki/append adds the given
Markdown as a new paragraph at the end, handy for building up a page from a
telnet client one block at a time. Every edit records a revision; see
[wiki administration].

To change a page's existing text, read its source with `@wiki/md` first (see
[wiki reading source]).

### Rolling back

@wiki/rollback restores the page body from an earlier revision (find the
number with `@wiki/history`; see [wiki administration]). The restore is a normal edit: it creates a NEW
revision rather than rewriting history, so a rollback can itself be rolled
back. The web portal offers the same action via the Restore button in each
page's history dialog.

### Translating

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

## Administration commands

- `@wiki/delete <page>` - delete a page (`wiki.delete`)
- `@wiki/protect <page>`, `@wiki/unprotect <page>` - require `wiki.admin` to edit and delete the page, or stop requiring it (`wiki.admin`)
- `@wiki/publish <page>`, `@wiki/unpublish <page>` - publish or mark as draft (`wiki.admin`)
- `@wiki/require <target>=<action> <permission>...` - what a namespace, category or page requires (`wiki.admin`)
- `@wiki/access <target>[=<player>]` - show what a target requires, or what a player may do with a page
- `@wiki/pin <category>`, `@wiki/unpin <category>` - show a category on the wiki home, or stop showing it (`wiki.admin`)

The first three are described in [wiki administration], the next two in
[wiki permissions], and pins in [wiki categories].

## Wiki administration

- `@wiki/delete <page>`
- `@wiki/protect <page>` and `@wiki/unprotect <page>`
- `@wiki/publish <page>` and `@wiki/unpublish <page>`
- `@wiki/history <page>`

Deleting needs `wiki.delete` and whatever the page requires to edit and delete
it; protecting and publishing need `wiki.admin`. Deletion removes the page and
its entire revision history. Protecting a page is a page requirement of
`wiki.admin` to edit and delete it, in-game and on the web portal alike; it
shows in `@wiki/access` and is cleared by `@wiki/unprotect`. Unpublished pages
are drafts: hidden from anonymous web visitors and from the sitemap, and
in-game their body and revision history are shown only to their author or a
`wiki.drafts` holder who asks for them with `/draft`. See [wiki reading drafts].

### Revision history

@wiki/history lists every revision with its editor, date, and edit summary, for
the revision stream of your own locale. Each locale is numbered independently
starting from 1, so "rev 3" of a French translation is unrelated to "rev 3" of
the source; `@wiki/history/source` shows the source locale's stream. An edit
summary is prose about unpublished content, so a draft's revisions are withheld
exactly as its body is: `@wiki/history/draft` shows them, to a `wiki.drafts`
holder or the author.

::: seealso
- [wiki]
- [wiki editing]
- [wiki permissions]
:::

## Wiki permissions

- `@wiki/require <target>=<action> [<permission> ...][, <action> ...]`
- `@wiki/access <target>`
- `@wiki/access <page>=<player>`

Every wiki action has a permission everybody needs for it: `wiki.read`,
`wiki.create`, `wiki.edit` and `wiki.delete`. On top of that, a namespace, a
category or a single page can require more, as MediaWiki's namespace and page
protections do. A target is `namespace <name>`, `category <name>`, or a page
title as `@wiki` takes it.

The wiki's own permissions:

| Permission | Lets you |
|---|---|
| `wiki.read` | read pages |
| `wiki.create` | create pages |
| `wiki.edit` | edit and translate pages |
| `wiki.delete` | delete pages |
| `wiki.drafts` | read other people's drafts (see [wiki reading drafts]) |
| `wiki.admin` | protect, publish and set requirements; skips namespace, category and page requirements |

### How requirements combine

A page requires everything its namespace, each of its categories and the page
itself require, for the action and for reading it; deleting also requires what
editing does. Requirements only add: no level lifts what another requires. A
player needs every permission listed, and a permission comes from their roles,
like any other (see [roles]). `wiki.admin` skips namespace, category and page
requirements, though not the four global permissions.

Filing a page in a category is an edit under its current categories and under
the new ones, so a category that requires `lore.edit` to edit can only gain
pages from someone holding it. Creating a page needs what its namespace
requires to create it and what the categories it starts in require.

New games start with the system namespace requiring `wiki.admin` to create,
edit and delete its pages.

### Setting and checking requirements

`@wiki/require` replaces what the actions it names require and leaves the rest
alone; an action with no permissions after it requires nothing again. The
permissions must be built-in or defined with `@permission/define` (see
[@permission define]). It needs `wiki.admin`.

`@wiki/access <target>` lists what a target requires; for a page, also what it
inherits. `@wiki/access <page>=<player>` says, action by action, whether that
player may act on the page and which requirement stops them. Asking about
anyone but yourself needs `wiki.admin`. A page you may not read answers as one
that does not exist.

```sharp
> @wiki/require category lore=edit lore.edit, read
WIKI: Category lore now requires:
    edit    lore.edit
> @wiki/access combat_primer=*Alice
WIKI: What Alice may do with 'Combat Primer':
  read    allowed
  edit    category lore requires lore.edit
  delete  needs wiki.delete
```

From softcode, `wikiaccess(<page>, <action>[, <player>])` returns 1 or 0.

::: seealso
- [wiki administration]
- [WIKIACCESS()]
- [roles]
- [@permission]
:::

## Wiki categories

- `@wiki/category <name>`
- `@wiki/category <page>=<name>[, <name>...]`
- `@wiki/pin [<category>]`
- `@wiki/unpin <category>`

Categories work the way they do in MediaWiki, except that they are held by
the page rather than written in its text. A page can be in any number of
categories; the portal lists them at the page's foot.

`@wiki/category <page>=<names>` replaces the page's categories with a
comma-separated list, and `@wiki/category <page>=` takes it out of all of them.
It is an edit: the page's requirements apply, and so do the new categories'
(see [wiki permissions]). It does not change the text, so it adds no revision.

Every page in the character namespace is in the Character category, whatever
its list says. The category comes from the namespace: it is added when the
page is written and put back if a list leaves it out, so it cannot be removed.

### Category pages

Each category has a page of its own, `Category:Lore`, in the category
namespace. Filing a page in a category that has no page yet makes one, titled
with the name exactly as you typed it, so `Places of Note` keeps its capitals
(when you may create pages there; a name typed in lower case, like `lore`,
makes none). Writing more on that page is optional: whatever it says is shown
above the list of the category's members. A category page that is itself in the Setting
category is a subcategory of Setting. `[[Category:Lore]]` in a page's text is
an ordinary link to that page.

A translation is in the same categories as its page; only a category's name
is translated. The name shown is the title of the category's page, so giving
`Category:Lore` a French translation in the portal shows French readers its
French title wherever the category appears. A category with no page is shown
by its key, with a capital first letter.

### Listing a category

`@wiki/category <name>` lists a category's subcategories and pages. Category
names are matched as titles are: case and spaces versus underscores do not
matter. From softcode, `wikicategory(<name>)` returns a category's pages and
`wiki(<page>, categories)` a page's categories.

```sharp
> @wiki/category combat_primer=Rules, Combat
WIKI: 'Combat Primer' categories: Combat, Rules.
> @wiki/category rules
```

The reply opens with a header line, `WIKI: Category 'Rules'` followed by a
dash and `1 page(s), 0 subcategory(ies):`, then one line per member:

```sharp
  main:combat_primer             Combat Primer (rev 2, 2026-10-05)
```

### Pinning categories

The portal's wiki home shows a card for each pinned category, with its newest
pages. Other categories are still listed in the wiki sidebar, and a search on
the wiki home looks through all of them. A new game pins Character.

`@wiki/pin <category>` pins a category and `@wiki/unpin <category>` unpins
it; both need `wiki.admin`. A category need not have a page or any member to
be pinned, and a leading `category:` is accepted. `@wiki/pin` alone lists the
pinned categories. Each form returns the pinned category keys, separated by
spaces. Staff can also pin a category from its page or from the Categories
card on the portal's wiki admin page.

```sharp
> @wiki/pin Rules
WIKI: Category 'Rules' is now pinned to the wiki home.
> @wiki/pin
WIKI: Pinned to the wiki home: Character, Rules.
```

::: seealso
- [wiki]
- [wiki editing]
- [WIKICATEGORY()]
:::
