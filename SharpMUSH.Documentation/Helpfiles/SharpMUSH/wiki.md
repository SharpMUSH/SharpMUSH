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
      "id": "categories",
      "heading": "Wiki categories",
      "lookup": "wiki categories",
      "aliases": [
        "WIKI-CATEGORIES",
        "@WIKI/CATEGORY"
      ]
    },
    {
      "id": "viewing-and-discovery",
      "heading": "Viewing and discovery",
      "lookup": "wiki viewing and discovery"
    },
    {
      "id": "authoring-commands",
      "heading": "Authoring commands",
      "lookup": "wiki authoring commands"
    },
    {
      "id": "administration-commands",
      "heading": "Administration commands",
      "lookup": "wiki administration commands"
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
      "heading": "Locale selection",
      "lookup": "wiki locale selection"
    },
    {
      "id": "example",
      "heading": "Example",
      "lookup": "wiki example"
    }
  ],
  "redirects": {}
}
-->
# wiki

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

Listings (`@wiki/list`, `@wiki/search`, `@wiki/recent`) print every page's
identifier fully qualified as `<namespace>:<slug>` — `home` in the main
namespace lists as `main:home`. One column, one grammar, and the identifier
printed is always one you can paste straight back into `@wiki`. Typing a
target is unchanged: the short form still works, so `@wiki home` and
`@wiki main:home` reach the same page.

## Viewing and discovery
* `@wiki <page>` or `@wiki/view <page>` - display a page
* `@wiki/list [<namespace>]` - list pages
* `@wiki/category <name>` - list the pages and subcategories in a category
* `@wiki/category <page>=<names>` - set a page's categories (comma-separated)
* `@wiki/search <text>` - find pages by title or content, in any locale
* `@wiki/recent [<count>]` - recently edited pages (default 10)
* `@wiki/history <page>` - revision history
* `@wiki/md <page>` - show the page's markdown source instead of the rendered
body
* `@wiki/view/draft <page>` - also render the page if it is a draft (wizard)

## Authoring commands
* `@wiki/create <title>=<markdown>` - create a page
* `@wiki/edit <page>=<markdown>` - replace a page's content
* `@wiki/append <page>=<markdown>` - add a paragraph to a page
* `@wiki/rollback <page>=<revision #>` - restore an earlier revision
* `@wiki/translate <page>/<lang>=<markdown>` - write one locale's translation

## Administration commands
* `@wiki/delete <page>` - delete a page (wizard)
* `@wiki/protect <page>`, `@wiki/unprotect <page>` - restrict edits to wizards (wizard)
* `@wiki/publish <page>`, `@wiki/unpublish <page>` - publish or mark as draft (wizard)

The `/noeval` switch may be combined with any of the above to suppress
softcode evaluation of the arguments.

## Reading source
`@wiki/md <page>` prints the markdown a page is stored with, exactly as stored -
nothing is rendered, reflowed or wrapped, and the page's own `[`, `%` and `$`
reach you as text. That is the copy you edit: read it with `/md`, change it, and
put it back with `@wiki/edit <page>=<markdown>`. `@wiki/view/source/md <page>`
gives a translator the source locale's markdown to work from before writing
`@wiki/translate`. The softcode equivalent is `wiki(<page>, markdown)`.

## Reading drafts
An unpublished page is a draft, and `@wiki` does not render a draft's body or
its revision history to anybody by default. You are told the page is a draft
rather than told it does not exist — the header still names it and marks it
`(draft)`.

* `@wiki/view/draft <page>` - render the draft's body as well
* `@wiki/history/draft <page>` - list the draft's revisions as well

`/draft` is an opt-in, not a permission: only a wizard can read a draft, and
for everybody else the answer is exactly the same with the switch as without
it, so `/draft` can never be used to find out whether a draft exists. The same
rule covers an unpublished *translation*: you get the published version in
whatever language the page does have, not a withheld French draft.

## Locale selection
@wiki reads pages in your locale, the one you set with `@locale`. When a page
has no translation in your locale you get the fallback version, and its locale
appears in brackets next to the revision number on the header line.

* `@wiki/view/source <page>` - read the page in the locale it was written in,
ignoring yours. Useful when translating.
* `@wiki/history <page>` shows your locale's revision stream, which is numbered
separately from the source's; `@wiki/history/source <page>` shows the source
locale's.
* `@wiki/translate <page>/<lang>=<markdown>` - write the `<lang>` translation.
See [wiki editing].

Like `/noeval`, `/source` and `/md`, `/draft` is a modifier rather than an
action, so it combines with `/view` and `/history` instead of replacing them
(`/draft` has no effect on `/search`, which already includes drafts for anyone
who can see them; `/md` has none on `/history` or the listings).

The modifiers are orthogonal and stack: `/source` chooses the locale, `/md`
chooses raw over rendered, `/draft` decides whether an unpublished body is shown
at all. `/md` grants nothing - a draft still needs `/draft` and still needs the
wizard bit, so asking for a draft's source is still asking for a draft.

Your `LOCALE` decides what you *read*. It never decides what you *write*:
`@wiki/translate` takes the language in the command and refuses to run without
it. Getting a read wrong shows you the wrong translation, which you can see and
undo; getting a write wrong files your English under French, which you cannot.

`@wiki/search` matches every locale a page has been translated into, not just
the one it was written in, so you find a page by whatever wording you remember.
Each page is listed once however many of its locales matched; when the text
that matched was not the page's own source locale, that locale appears in
brackets after the line, and if several locales matched, yours is the one
shown. `@wiki/search/source <text>` matches source text only.

Drafts stay out of the way: unpublished pages and unpublished translations are
shown to wizards only — `@wiki/list`, `@wiki/search` and `@wiki/recent` omit
them, and `wiki()`, `wikilist()`, `wikisearch()` and `wikirecent()` never return
them. `@wiki/list`'s totals count only what you are allowed to see, so the count
does not give a draft away either. Being able to *edit* a page is not enough:
publishing is wizard-only, so the wizard bit is the one thing that tracks who
may know a draft exists.

Even a wizard reads a draft's body or history only by adding `/DRAFT` — see
[wiki]. A page that is itself unpublished stays withheld however its
individual translations are flagged, so publishing one language does not
publish the article.

That keeps a draft's title, text and existence out of discovery. It is still not
a secret store: asking for a page by name tells you whether one is there. Put
nothing in a draft that the people who can guess its name should not know exists.

Page content is Markdown; see `help markdown` or the wiki's own
"Help:Markdown Guide" page (`@wiki help:markdown_guide`) for the supported
syntax. Live listing blocks (`::: category ...`) render on the web portal and
appear in-game as a placeholder.

Linking to other pages:
Inside a page's Markdown, `[[Page Name]]` links to another wiki page. The
target takes the same forms `@wiki` does, so `[[Home]]` and
`[[Help:Markdown Guide]]` reach the pages you would reach by typing them. `[[Display text|Page Name]]` links with
wording of your own; without it the page's title is shown. Only the display
text appears - the brackets and the target never do.

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

::: seealso
- [wiki editing]
- [wiki administration]
- [wiki categories]
- [WIKI()]
:::

## Editing wiki pages

- `@wiki/create <title>=<markdown>`
- `@wiki/edit <page>=<markdown>`
- `@wiki/append <page>=<markdown>`
- `@wiki/rollback <page>=<revision #>`
- `@wiki/translate <page>/<lang>=<markdown>`

@wiki/create makes a new wiki page. The title may carry a namespace prefix
(`@wiki/create Help:House Rules=# House Rules`); the page's URL slug is
derived from the title (lower-case, spaces become underscores).

@wiki/edit replaces a page's entire Markdown body. @wiki/append adds the given
Markdown as a new paragraph at the end — handy for building up a page from a
telnet client one block at a time. Every edit records a revision; see
[wiki administration].

@wiki/rollback restores the page body from an earlier revision (find the
number with [wiki administration]). The restore is a normal edit: it creates a NEW
revision rather than rewriting history, so a rollback can itself be rolled
back. The web portal offers the same action via the Restore button in each
page's history dialog.

@wiki/translate writes one locale's translation of a page — the same rows the
web portal's language selector edits. The language is part of the target,
after a slash: `@wiki/translate combat_primer/fr=...`. It is required, and it
is never taken from your `LOCALE`; without it (or with a language tag SharpMUSH
does not recognise) the command refuses and writes nothing. `@wiki/edit` writes
the page itself, so translating a page into the language it was written in is
refused too.

A translation keeps its own title, revision numbers and draft flag; the page's
categories and protection are inherited and cannot differ. @wiki/translate
supplies only the body, so an existing translation keeps the title and the
draft/published state it already had, and a brand-new one starts published,
under the source page's title. Retitle it on the web portal.

If somebody else saves the same translation between your reading it and your
sending the command, you are told so and *nothing is written* — re-read the
page and re-apply your text. The command never retries by itself, because a
retry would put your older text on top of theirs.

Protected pages can only be edited by wizards, translations included. Each page
records its author and last editor by dbref.

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
- [wiki]
- [wiki administration]
:::

## Wiki administration

- `@wiki/delete <page>`
- `@wiki/protect <page>` and `@wiki/unprotect <page>`
- `@wiki/publish <page>` and `@wiki/unpublish <page>`
- `@wiki/history <page>`

Deleting, protecting, and publishing are wizard-only. Deletion removes the
page and its entire revision history. Protected pages refuse edits from
non-wizards both in-game and on the web portal. Unpublished pages are drafts:
hidden from anonymous web visitors and from the sitemap, and in-game their
body and revision history are shown only to a wizard who asks for them with
`/draft`. See [wiki].

@wiki/history lists every revision with its editor, date, and edit summary, for
the revision stream of your own locale. Each locale is numbered independently
starting from 1, so "rev 3" of a French translation is unrelated to "rev 3" of
the source; `@wiki/history/source` shows the source locale's stream. An edit
summary is prose about unpublished content, so a draft's revisions are withheld
exactly as its body is: `@wiki/history/draft` shows them, to a wizard.

::: seealso
- [wiki]
- [wiki editing]
:::

## Wiki categories

- `@wiki/category <name>`
- `@wiki/category <page>=<name>[, <name>...]`

Categories work the way they do in MediaWiki, except that they are held by
the page rather than written in its text. A page can be in any number of
categories; the portal lists them at the page's foot.

`@wiki/category <page>=<names>` replaces the page's categories with a
comma-separated list, and `@wiki/category <page>=` takes it out of all of them.
It follows the same rule as @wiki/edit: a protected page is wizard-only. It is
not an edit of the text, so it adds no revision.

Each category has a page of its own, `Category:Lore`, in the category
namespace. Writing that page is optional: whatever it says is shown above the
list of the category's members. A category page that is itself in the Setting
category is a subcategory of Setting. `[[Category:Lore]]` in a page's text is
an ordinary link to that page.

A translation is in the same categories as its page; only a category's name
is translated. The name shown is the title of the category's page, so giving
`Category:Lore` a French translation in the portal shows French readers its
French title wherever the category appears. A category with no page is shown
by its name as typed.

`@wiki/category <name>` lists a category's subcategories and pages. Category
names are matched as titles are: case and spaces versus underscores do not
matter. From softcode, `wikicategory(<name>)` returns a category's pages and
`wiki(<page>, categories)` a page's categories.

```sharp
> @wiki/category combat_primer=Rules, Combat
WIKI: 'Combat Primer' categories: Combat, Rules.
> @wiki/category rules
WIKI: Category 'Rules' — 1 page(s), 0 subcategory(ies):
  main:combat_primer             Combat Primer (rev 2, 2026-10-05)
```

::: seealso
- [wiki]
- [wiki editing]
:::
