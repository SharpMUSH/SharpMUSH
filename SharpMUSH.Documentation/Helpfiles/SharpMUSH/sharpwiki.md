# wiki()

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
* `categories` - the categories the page is in, as keys, space-separated.
  A translation has no categories of its own: every locale answers the same
* `namespace` - main, help, character, system, or category
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

Unpublished pages and unpublished translations are never reachable from
`wiki()`. Softcode has no reader to check drafts against, so a draft page
answers exactly as a missing one does; there is no field, not even `title`,
that reveals it. Read a draft with `@wiki/view/draft` instead.

Pages you may not read (see [wiki permissions]) are treated the same way.

Returns #-1 NO SUCH WIKI PAGE when the page does not exist, is a draft, or is
one you may not read.

## Example

```sharp
> think wiki(home, title)
Home
> think wiki(help:markdown_guide, revision)
1
> think wiki(markdown_guide, locale, fr)
en
```

::: seealso
- [WIKICATEGORY()]
- [WIKILIST()]
- [WIKISEARCH()]
- [WIKIRECENT()]
- [WIKIACCESS()]
:::

# wikicategory()

- `wikicategory(<category>)`

Returns a space-separated list of the references of the published pages in a
category, its subcategories (pages in the `category` namespace) included. The
name is matched the way the portal matches it, so `Places of Note`,
`places of note` and `places_of_note` name one category, and a leading
`category:` is accepted. Categories belong to the page, not to a translation,
so the list is the same whatever your `LOCALE` is.

The other direction, the categories one page is in, is `wiki(<page>, categories)`.
Set them with `@wiki/category <page>=<name>, <name>`.

## Example

```sharp
> think wikicategory(Help)
help:markdown_guide
> think wiki(help:markdown_guide, categories)
help
```

::: seealso
- [WIKI()]
- [WIKILIST()]
- [wiki]
:::

# wikilist()

- `wikilist()`
- `wikilist(<namespace>)`

Returns a space-separated list of wiki page references, optionally restricted
to one namespace. Main-namespace pages appear as their slug; other pages as
`namespace:slug`; both forms are valid inputs to [WIKI()] and `@wiki`.

A page reference is its canonical slug, which does not change between locales,
so this list is the same whatever your `LOCALE` is. Pass a reference from it to
[WIKI()] with a locale to read that page's translation.

## Example

```sharp
> think wikilist(help)
help:markdown_guide
```

::: seealso
- [WIKI()]
- [WIKISEARCH()]
:::

# wikisearch()

- `wikisearch(<text>)`

Returns a space-separated list of page references whose title or content
contains *<text>* (case-insensitive), in any locale the page has been
translated into. `*` and `?` in *<text>* are wildcards, matched anywhere in the
title or content: `wikisearch(home*)` finds what `wikisearch(home)` does. Each page appears once however many of its locales matched.
Unpublished pages and unpublished translations are never returned. Limited to
the first 100 matches.

## Example

```sharp
> think wikisearch(combat)
combat_primer house_rules
```

::: seealso
- [WIKI()]
- [WIKILIST()]
:::

# wikirecent()

- `wikirecent()`
- `wikirecent(<count>)`

Returns a space-separated list of the most recently edited page references,
newest first. *<count>* defaults to 10 and is clamped to 1-50.

::: seealso
- [WIKI()]
- [WIKILIST()]
:::

# wikiaccess()

- `wikiaccess(<page>, <action>)`
- `wikiaccess(<page>, <action>, <player>)`

Returns 1 when you, or *<player>*, may take *<action>* on a wiki page, and 0
when not. *<action>* is `read`, `edit` or `delete`. The answer weighs the
global permission for the action and everything the page's namespace,
categories and the page itself require; see [wiki permissions]. `@wiki/access`
says why.

Asking about another player needs `wiki.admin`. A page you may not read, or a
draft, returns #-1 NO SUCH WIKI PAGE, as [WIKI()] does.

## Example

```sharp
> think wikiaccess(combat_primer, edit)
1
> think wikiaccess(combat_primer, edit, *Alice)
0
```

**See Also:**
- [WIKI()]
- [wiki permissions]
