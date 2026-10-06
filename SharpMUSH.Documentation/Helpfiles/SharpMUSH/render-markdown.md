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
    }
  ]
}
-->
# RENDERMARKDOWN()

`rendermarkdown([<markdown>[, <width>]])`

Renders CommonMark/Markdown text into SharpMUSH MarkupString with ANSI formatting. This function converts markdown syntax into formatted text with ANSI color codes and styles for display in MUSH clients.

### Parameters
- `<markdown>` - The markdown/CommonMark text to render. Supports all standard CommonMark features.
- `<width>` - Optional. Maximum width for rendered output (default: 78). Must be between 10-1000. Tables automatically fit to this width with proportional column spacing.

## Supported Markdown Features
- **Text Formatting**: `**bold**`, `*italic*`, `` `code` ``
- **Headings**: `# H1`, `## H2`, `### H3` (rendered with ANSI underline and bold)
- **Links**: `[text](url)` or `<url>` (rendered as ANSI OSC 8 hyperlinks, clickable in compatible terminals)
- **Lists**: Ordered (`1. Item`) and unordered (`- Item`) with proper indentation and ANSI-styled bullets
- **Tables**: Pipe-delimited tables with column alignment (`:---` left, `:---:` center, `---:` right)
- **Code Blocks**: Triple-backtick fenced code blocks with optional language tag for syntax highlighting (see below)
- **Block Quotes**: `> Quote` rendered with 2-space indentation
- **See Also footers**: a `::: seealso` block holding a list of `[topic]` or `` `code` `` names prints as one line, `See Also: @lock, @unlock`, wrapped under the first topic. If any item says more than its name, the list prints as a list under the label
- **HTML Entities**: `&amp;`, `&lt;`, etc.

## Syntax Highlighting in Code Blocks
Fenced code blocks support ANSI syntax highlighting when a language tag is specified.

Use `` ```sharp `` for SharpMUSH/MUSH softcode — the full semantic token pipeline<br>
(functions, substitutions, object references, registers, etc.) is used:

```sharp
name(%#)              -- function call + substitution
get(#1/ATTR)          -- object reference
%q<myvar>             -- register read
```

Standard programming languages are also supported via ColorCode and render with<br>
`StyleDictionary.DefaultDark` colours:

```json
{"hello": 42, "active": true}
```

```python
def greet(name):
    return "Hello, " + name
```

Other supported language tags: `csharp`, `javascript`, `typescript`, `sql`, `xml`,<br>
`html`, `css`, `java`, `powershell`, `fsharp`, `python`, `json`, `cpp`.

Blocks without a language tag, or with an unrecognised tag, fall back to plain<br>
2-space-indented text with no colour.

## MUSH Special Character Escaping
When using markdown features with square brackets `[ ]` or parentheses `(` `)`, you must escape them using `%`:
- `%[` for `[`
- `%]` for `]`
- `%(` for `(`
- `%)` for `)`

## Examples
Basic text formatting:
```sharp
think rendermarkdown(This is **bold** and *italic* text)
```
Output:
```text
This is bold and italic text
```
(with ANSI codes for bold and italic styling)

Headings:
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

Links (note the escaping):
```sharp
think rendermarkdown(%[Click here%]%(https://example.com%))
```
Output:
```text
Click here
```
(with ANSI OSC 8 hyperlink - clickable in compatible terminals)

Tables:
```sharp
think rendermarkdown(| Name | Age |%r|------|-----|%r| Alice | 30 |%r| Bob | 25 |)
```
Output:
```markdown
| Name                          | Age                           |
|-------------------------------|-------------------------------|
| Alice                         | 30                            |
| Bob                           | 25                            |
```
(table expands to use default 78 character width, borders styled with ANSI faint)

Tables with custom width:
```sharp
think rendermarkdown(| Name | Age |%r|------|-----|%r| Alice | 30 |, 50)
```
Output:
```markdown
| Name              | Age               |
|-------------------|-------------------|
| Alice             | 30                |
| Bob               | 25                |
```
(table fits within 50 character width)

Code blocks with syntax highlighting (use `sharp` tag for SharpMUSH softcode):
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

Code blocks (plain, no language tag):
```sharp
think rendermarkdown(``````%rvar x = 42;%rvar y = 100;%r```````)
```
Output:
```text
  var x = 42;
  var y = 100;
```
(2-space indentation, no colour)

Ordered lists:
```sharp
think rendermarkdown(1. First item%r2. Second item%r3. Third item)
```
Output:
```markdown
1. First item
2. Second item
3. Third item
```
(numbers styled with ANSI faint)

Unordered lists:
```sharp
think rendermarkdown(- First item%r- Second item%r- Third item)
```
Output:
```markdown
- First item
- Second item
- Third item
```
(bullets styled with ANSI faint)

Block quotes:
```sharp
think rendermarkdown(> This is a quote%r> spanning multiple lines)
```
Output:
```text
  This is a quote
  spanning multiple lines
```
(2-space indentation on all quote lines)

### Error Handling
- Returns `#-1 INVALID WIDTH (must be 10-1000)` if width parameter is out of range
- Returns `#-1 ERROR RENDERING MARKDOWN: <error>` if markdown parsing fails

### Notes
- Tables automatically expand to use full available width for professional spacing
- Links use ANSI OSC 8 hyperlinks, making them clickable in compatible terminals (iTerm2, Windows Terminal, etc.)
- All structural elements (borders, bullets) use ANSI faint/dim styling for visual distinction
- Output is proper MarkupString with embedded ANSI codes

::: seealso
- [rendermarkdowncustom()]
- `[MARKUP]`
- [ANSI]
:::
