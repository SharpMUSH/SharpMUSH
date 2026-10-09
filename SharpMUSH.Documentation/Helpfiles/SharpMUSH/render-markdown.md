<!-- help-article
{
  "corpus": "help",
  "id": "render-markdown",
  "lookup": "rendermarkdown()",
  "aliases": [
    "rendermarkdown"
  ],
  "sections": [
    {
      "id": "supported-markdown-features",
      "heading": "Supported Markdown Features",
      "lookup": "rendermarkdown supported markdown features"
    },
    {
      "id": "syntax-highlighting-in-code-blocks",
      "heading": "Syntax Highlighting in Code Blocks",
      "lookup": "rendermarkdown syntax highlighting in code blocks"
    },
    {
      "id": "mush-special-character-escaping",
      "heading": "MUSH Special Character Escaping",
      "lookup": "rendermarkdown mush special character escaping"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "rendermarkdown examples"
    },
    {
      "id": "table-examples",
      "heading": "Table examples",
      "lookup": "rendermarkdown table examples"
    },
    {
      "id": "code-examples",
      "heading": "Code block examples",
      "lookup": "rendermarkdown code examples"
    },
    {
      "id": "list-examples",
      "heading": "List and quote examples",
      "lookup": "rendermarkdown list examples"
    }
  ]
}
-->
# rendermarkdown()

`rendermarkdown([<markdown>[, <width>]])`

Renders CommonMark/Markdown text into SharpMUSH MarkupString with ANSI formatting. This function converts markdown syntax into formatted text with ANSI color codes and styles for display in MUSH clients. The output is a MarkupString with the ANSI styling embedded, so it can be passed on to other functions.

### Parameters

- `<markdown>`: the markdown/CommonMark text to render. Supports all standard CommonMark features.
- `<width>`: optional. Maximum width for rendered output (default: 78). Must be between 10 and 1000. Tables, lists, rules and flex layouts are laid out to this width, and a client that reports a different width is sent them laid out again to its own.

### Errors

- `#-1 INVALID WIDTH (MUST BE 10-1000)` if `<width>` is not a number in that range.
- `#-1 ERROR RENDERING MARKDOWN: <error>` if rendering the markdown fails.

### Sections

- [rendermarkdown supported markdown features]: what each Markdown element renders as.
- [rendermarkdown syntax highlighting in code blocks]: language tags on fenced code.
- [rendermarkdown mush special character escaping]: writing `[ ]` and `( )` inside the argument.
- [rendermarkdown examples]: one example per element.

## Supported Markdown Features

### Inline formatting

- **Text Formatting**: `**bold**`, `*italic*`, `` `code` ``
- **Links**: `[text](url)` or `<url>` (rendered as ANSI OSC 8 hyperlinks, clickable in compatible terminals such as iTerm2 and Windows Terminal)
- **Images**: `![alt](url)` reads `[image: alt]` (or `[image]`). When the caller may use [image()], a client that draws pictures (the web terminal, MXP, Pueblo) is shown the picture instead, held to `image_hosts` the same way. An image alone in its paragraph is laid out as a [figure()]; one inside a sentence stays in the line. A pixel size in `{width=200 height=100}` after the image is kept; a percentage is not.
- **HTML Entities**: `&amp;`, `&lt;`, etc.

### Blocks

- **Headings**: `# H1`, `## H2`, `### H3` (rendered with ANSI underline and bold)
- **Lists**: Ordered (`1. Item`) and unordered (`- Item`); a long item wraps under its own text, not under its marker. Bullets are ANSI faint.
- **Tables**: Pipe-delimited tables with column alignment (`:---` left, `:---:` center, `---:` right). Each column is as wide as its widest cell; on a narrow client the widest columns wrap first, then the rightmost columns are left out. The separators are ANSI faint. The web portal shows a real table.
- **Rules**: `---` on a line of its own draws an ANSI faint line across the width.
- **Code Blocks**: Triple-backtick fenced code blocks with optional language tag for syntax highlighting (see [rendermarkdown syntax highlighting in code blocks]).
- **Block Quotes**: `> Quote` rendered with 2-space indentation.

### Containers and layout

- **See Also footers**: a `::: seealso` block holding a list of `[topic]` or `` `code` `` names prints as one line, `See Also: @lock, @unlock`, wrapped under the first topic. If any item says more than its name, the list prints as a list under the label.
- **Centred blocks**: everything between `::: center` and a closing `:::` is wrapped to `<width>` and each line centred in it. The wiki and the web portal centre the same block on the page.
- **Layout**: a `:::: flex` block holding `::: item` blocks prints its items side by side as columns, each rendered at its own width.
  - Settings go in `{...}` after the name: `grow`, `basis`, `min`, `gap`, `align`, `justify`, `direction`, `wrap`. The wiki's Markdown Guide lists every setting.
  - A block holding others needs more colons than they have.
  - Items narrower than their `min` (24 by default) stack instead.

## Syntax Highlighting in Code Blocks

Fenced code blocks support ANSI syntax highlighting when a language tag is specified.

### SharpMUSH softcode

Use `` ```sharp `` for SharpMUSH/MUSH softcode, which gets the full semantic token pipeline (functions, substitutions, object references, registers, etc.):

```sharp
name(%#)              -- function call + substitution
get(#1/ATTR)          -- object reference
%q<myvar>             -- register read
```

### Other languages

Standard programming languages are also supported, and render in a dark-background colour scheme:

```json
{"hello": 42, "active": true}
```

```python
def greet(name):
    return "Hello, " + name
```

Other supported language tags: `csharp`, `javascript`, `typescript`, `sql`, `xml`, `html`, `css`, `java`, `powershell`, `fsharp`, `python`, `json`, `cpp`.

### No language tag

Blocks without a language tag, or with an unrecognised tag, fall back to plain 2-space-indented text with no colour.

## MUSH Special Character Escaping

When using markdown features with square brackets `[ ]` or parentheses `(` `)`, you must escape them using `%`:

- `%[` for `[`
- `%]` for `]`
- `%(` for `(`
- `%)` for `)`

See [rendermarkdown examples] for a link written this way.

## Examples

### Text formatting

```sharp
think rendermarkdown(This is **bold** and *italic* text)
```

Output:

```text
This is bold and italic text
```

(with ANSI codes for bold and italic styling)

### Headings

```sharp
think rendermarkdown(# My Heading%r%rThis is a paragraph)
```

Output:

```text
My Heading
==========

This is a paragraph
```

(heading is underlined and bold with ANSI codes)

### Links

Note the escaping:

```sharp
think rendermarkdown(%[Click here%]%(https://example.com%))
```

Output:

```text
Click here
```

(with ANSI OSC 8 hyperlink - clickable in compatible terminals)

## Table examples

### Tables

```sharp
think rendermarkdown(| Name | Age |%r|------|-----|%r| Alice | 30 |%r| Bob | 25 |)
```

Output:

```text
Name  | Age
-----------
Alice | 30
Bob   | 25
```

(the separators and the line under the headings are ANSI faint)

### Tables wider than the width

```sharp
think rendermarkdown(| Topic | Summary |%r|---|---|%r| @lock | Sets a lock on an object, which decides who may pass it. |, 40)
```

Output:

```text
Topic | Summary
----------------------------------------
@lock | Sets a lock on an object, which
      | decides who may pass it.
```

(the widest column wraps to fit 40 characters)

## Code block examples

### Highlighted code blocks

Use the `sharp` tag for SharpMUSH softcode:

```sharp
think rendermarkdown(``````sharp%rname(%#)%rget(#1/ATTR)%r```````)
```

Output (with ANSI colour):

```text
  name(                    <- yellow (function call)
  %#                       <- bright blue (substitution)
  )
  get(#1/ATTR)             <- yellow / light blue (function + object ref)
```

### Plain code blocks

With no language tag:

```sharp
think rendermarkdown(``````%rvar x = 42;%rvar y = 100;%r```````)
```

Output:

```text
  var x = 42;
  var y = 100;
```

(2-space indentation, no colour)

## List and quote examples

### Ordered lists

```sharp
think rendermarkdown(1. First item%r2. Second item%r3. Third item)
```

Output:

```text
1. First item
2. Second item
3. Third item
```

### Unordered lists

```sharp
think rendermarkdown(- First item%r- Second item%r- Third item)
```

Output:

```text
* First item
* Second item
* Third item
```

(bullets styled with ANSI faint)

### Block quotes

```sharp
think rendermarkdown(> This is a quote%r> spanning multiple lines)
```

Output:

```text
  This is a quote
  spanning multiple lines
```

(2-space indentation on all quote lines)

::: seealso
- [rendermarkdowncustom()]
- `[MARKUP]`
- [ANSI]
:::
