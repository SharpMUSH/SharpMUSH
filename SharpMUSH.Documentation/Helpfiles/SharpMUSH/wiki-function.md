<!-- help-article
{
  "corpus": "help",
  "id": "wiki-function",
  "lookup": "wiki()",
  "aliases": [],
  "sections": [
    {"id": "fields", "heading": "Fields", "lookup": "wiki() fields"},
    {"id": "locales", "heading": "Locales", "lookup": "wiki() locales"},
    {"id": "examples", "heading": "Examples", "lookup": "wiki() examples"}
  ]
}
-->
# wiki()

- `wiki(<page>)`
- `wiki(<page>, <field>)`
- `wiki(<page>, <field>, <locale>)`

Returns information about a wiki page. With one argument, returns the page's plain-text content. The page target accepts a namespace prefix (`wiki(help:markdown_guide)`).

Unpublished pages and unpublished translations are never reachable from `wiki()`. Softcode has no reader to check drafts against, so a draft page answers exactly as a missing one does; there is no field, not even `title`, that reveals it. Read a draft with `@wiki/view/draft` instead.

Pages you may not read (see [wiki permissions]) are treated the same way.

Returns #-1 NO SUCH WIKI PAGE when the page does not exist, is a draft, or is one you may not read.

- [wiki() fields]: the fields the second argument selects
- [wiki() locales]: the third argument, and fallback translations
- [wiki() examples]: reading a title, a revision and a locale

## Fields

The optional second argument selects a field:
* `text` - plain text content (the default)
* `markdown` - the raw Markdown source
* `title` - the display title
* `locale` - the locale actually served (see below)
* `categories` - the categories the page is in, as keys, space-separated. A translation has no categories of its own: every locale answers the same
* `namespace` - main, help, character, system, or category
* `revision` - the current revision number in the served locale
* `updated` - the last-edit time as a Unix timestamp (secs)
* `author` - the dbref of the page's creator

## Locales

The optional third argument names a locale; it defaults to your `LOCALE`. `text`, `markdown`, `title`, `revision` and `locale` come from the translation in that locale; everything else is page metadata and is the same in every locale. When there is no translation you get the fallback version rather than an error, and the `locale` field is how softcode detects that: it returns the locale that was served, not the one you asked for. An unparseable locale is treated as if you had passed none.

## Examples

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
