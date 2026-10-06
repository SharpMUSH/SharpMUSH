The wiki uses **CommonMark** Markdown with the extensions described below.
Raw HTML is **disabled** for security — typing `<b>`, `<img>`, or `<script>` renders
the text literally instead of acting as HTML. Everything you need has a Markdown
or SharpMUSH equivalent.

## Basic formatting

| You type | You get |
|---|---|
| `**bold**` | **bold** |
| `_italic_` | _italic_ |
| `` `code` `` | `code` |
| `~~strikethrough~~` | ~~strikethrough~~ |
| `# Heading` … `###### Heading` | section headings |
| `> quoted text` | a blockquote |
| `---` on its own line | a horizontal rule |

## Lists

```
- Unordered item
- Another item
    - Nested item

1. Ordered item
2. Second item

- [ ] Task still open
- [x] Task done
```

## Links

- External: `[link text](https://example.com)`
- **Wiki links**: `[[Page Name]]` links to a page in this wiki, displaying "Page Name".
- Custom text: `[[Display Text|Page Name]]`
- Other namespaces: `[[Help:Getting Started]]` or `[[Character:Some Name]]`
- A category's page: `[[Category:Lore]]`
- Bare URLs like `https://example.com` auto-link.

## Images

Upload images via **Insert image** in the editor (or *Admin → Wiki Assets*), then:

```
![alt text](/api/wiki-assets/<id>/<file>)
```

Size them with an attribute block right after the image — widths and heights are
plain numbers (pixels) or percentages:

```
![SharpMUSH logo](/assets/Logo.svg){width=200 height=100}
![Banner](/assets/Logo.svg){width=50%}
```

Only `width`, `height`, and CSS class names (`{.my-class}`) are honoured in
attribute blocks; anything else is stripped.

## Tables

```
| Column A | Column B |
|----------|----------|
| cell     | cell     |
```

## Code blocks

Fence multi-line code with triple backticks. A language hint after the opening
fence is preserved for highlighting:

````
```mushcode
@emit Hello, world!
```
````

## Live listing blocks (SharpMUSH extension)

Directive blocks render **live, always-current listings** when the page is viewed.
Each block opens with `:::` plus a directive and closes with a bare `:::`:

```
::: category lore
:::

::: pagelist help
:::

::: recent 10
:::
```

- `category <name>` — every page in a category
- `pagelist <namespace>` — every page in a namespace (main, help, character, system, category)
- `recent <count>` — the most recently edited pages (1–50)

For example, the five most recently edited pages right now:

::: recent 5
:::

## Centring (SharpMUSH extension)

CommonMark has no way to align text, so SharpMUSH adds one block. Everything
between `::: center` and a bare `:::` is centred on the web, and in-game
(`@wiki`, `rendermarkdown()`) each of its lines is centred in the text width,
78 columns by default:

```
::: center
# Welcome

A game of *small towns and long winters*.
:::
```

Headings, paragraphs, images and lists inside the block are all centred; a
list keeps its bullets beside its items. Text outside the block stays
left-aligned, including on the home page.

## Categories

A page's categories are set in the editor's **Categories** field (below the text
area), not in the text. A page can be in as many categories as you like; they are
listed at its foot, each linking to the category's page. Category names ignore
case, and spaces and underscores are the same. In-game, `@wiki/category
page=Lore, Places of Note` sets them.

Every category has its own page, `Category:Lore`, which lists its subcategories
and member pages. Writing that page is optional: whatever it says appears above
the list. A category page that is itself in the Setting category is a
subcategory of Setting. `[[Category:Lore]]` in text is an ordinary link to that
page.

## Publishing

The editor's **Published** switch (below the text area) controls whether a page
is live. Unpublished pages are drafts: invisible to visitors who aren't logged
in, and excluded from listings and the sitemap.
