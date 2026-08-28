# RENDERMARKDOWN()
`rendermarkdown(<markdown>[, <width>])`

Renders CommonMark/Markdown text into SharpMUSH MarkupString with ANSI formatting. This function converts markdown syntax into formatted text with ANSI color codes and styles for display in MUSH clients.

**Parameters:**
- `<markdown>` - The markdown/CommonMark text to render. Supports all standard CommonMark features.
- `<width>` - Optional. Maximum width for rendered output (default: 78). Must be between 10-1000. Tables automatically fit to this width with proportional column spacing.

**Supported Markdown Features:**
- **Text Formatting**: `**bold**`, `*italic*`, `` `code` ``
- **Headings**: `# H1`, `## H2`, `### H3` (rendered with ANSI underline and bold)
- **Links**: `[text](url)` or `<url>` (rendered as ANSI OSC 8 hyperlinks, clickable in compatible terminals)
- **Lists**: Ordered (`1. Item`) and unordered (`- Item`) with proper indentation and ANSI-styled bullets
- **Tables**: Pipe-delimited tables with column alignment (`:---` left, `:---:` center, `---:` right)
- **Code Blocks**: Triple-backtick fenced code blocks with optional language tag for syntax highlighting (see below)
- **Block Quotes**: `> Quote` rendered with 2-space indentation
- **HTML Entities**: `&amp;`, `&lt;`, etc.

**Syntax Highlighting in Code Blocks:**

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

**MUSH Special Character Escaping:**<br>
When using markdown features with square brackets `[` `]` or parentheses `(` `)`, you must escape them using `%`:
- `%[` for `[`
- `%]` for `]`
- `%(` for `(`
- `%)` for `)`

**Examples:**

Basic text formatting:
```sharp
think rendermarkdown(This is **bold** and *italic* text)
```
Output:
```
This is bold and italic text
```
(with ANSI codes for bold and italic styling)

Headings:
```sharp
think rendermarkdown(# My Heading%r%rThis is a paragraph)
```
Output:
```
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
```
Click here
```
(with ANSI OSC 8 hyperlink - clickable in compatible terminals)

Tables:
```
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
```
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
```
  name(                    <- yellow (function call)
  %#                       <- bright blue (substitution)
  )
  get(#1/ATTR)             <- yellow / light blue (function + object ref)
```

Code blocks (plain, no language tag):
```
think rendermarkdown(``````%rvar x = 42;%rvar y = 100;%r```````)
```
Output:
```javascript
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
```
  This is a quote
  spanning multiple lines
```
(2-space indentation on all quote lines)

**Error Handling:**
- Returns `#-1 INVALID WIDTH (must be 10-1000)` if width parameter is out of range
- Returns `#-1 ERROR RENDERING MARKDOWN: <error>` if markdown parsing fails

**Notes:**
- Tables automatically expand to use full available width for professional spacing
- Links use ANSI OSC 8 hyperlinks, making them clickable in compatible terminals (iTerm2, Windows Terminal, etc.)
- All structural elements (borders, bullets) use ANSI faint/dim styling for visual distinction
- Output is proper MarkupString with embedded ANSI codes

**See Also:**
- [rendermarkdowncustom()]
- [MARKUP]
- [ANSI]

# RENDERMARKDOWNCUSTOM()
`rendermarkdowncustom(<markdown>, <object>[, <width>])`

Renders CommonMark/Markdown text with customizable rendering controlled by attributes on the specified object. This allows you to define custom ANSI styles, colors, and formatting for each markdown element type.

**Parameters:**
- `<markdown>` - The markdown/CommonMark text to render
- `<object>` - Object reference (dbref) containing rendering template attributes
- `<width>` - Optional. Maximum width for rendered output (default: 78). Must be between 10-1000.

**Custom Template System:**

The function looks for attributes on `<object>` with specific names that define how to render each markdown element. These attributes contain softcode that is evaluated with markdown content passed as arguments. If a template attribute is not found, the default rendering is used.

**Supported Template Attributes:**

- ``RENDERMARKUP`H1`` - Heading level 1 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- ``RENDERMARKUP`H2`` - Heading level 2 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- ``RENDERMARKUP`H3`` - Heading level 3 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- ``RENDERMARKUP`CODEBLOCK`` - Code block rendering
  - `%0` - The code content (plain text, newlines separated)
  
- ``RENDERMARKUP`LISTITEM`` - List item rendering
  - `%0` - Is ordered list? (1 for ordered, 0 for unordered)
  - `%1` - Item number, 1-based (the first item is `1`)
  - `%2` - The list item content
  
- ``RENDERMARKUP`QUOTE`` - Block quote rendering
  - `%0` - The quote content (already rendered)
  
- ``RENDERMARKUP`TABLE`` - Table rendering. Receives the table described as a
  single JSON object, so a template can lay the table out itself
  - `%0` - The table, as JSON:
    ```json
    {
      "width": 78,
      "align": ["<", "-", ">"],
      "widths": [22, 22, 22],
      "head": ["Command", "Effect", "Cost"],
      "rows": [["`@wiki`", "Show the **index**", "0"]]
    }
    ```
  - `width` - the width this render was asked for. Not the same as `width(%#)`:
    the caller may have chosen a fixed width, and a table laid out to a
    different one disagrees with the prose around it
  - `align` - one entry per column: the `align()` justification character,
    `<` left, `-` centre, `>` right, and `<` for a column the table does not
    align. Ready to concatenate with a width into an `align()`/`lalign()` spec
  - `widths` - one integer per column, the width the default renderer would
    use. Computed from the *rendered* cell content, which is why a template
    cannot work it out from the raw source it is given: `**index**` is nine
    characters of source and five on screen
  - `head` - the header row's cells; an empty array when there is no header row
  - `rows` - the body rows, an array of arrays. A row shorter than the table is
    padded with empty cells, so every row has one cell per column
  
  There is no column count: `align` and `widths` already have exactly one entry
  per column. Read it with `json_query(json_query(%0,get,widths),size)`.

- ``RENDERMARKUP`CONTAINER`` - Custom container (`::: name args`) rendering
  - `%0` - The directive name (`category`, `tag`, `pagelist`, `recent`, or your own)
  - `%1` - The rest of the fence line, empty if there is none
  - `%2` - The container's rendered contents
  
- ``RENDERMARKUP`BOLD`` - Bold (`**text**`) rendering
  - `%0` - The bold content (already rendered)
  
- ``RENDERMARKUP`ITALIC`` - Italic (`*text*`) rendering
  - `%0` - The italic content (already rendered)
  
- ``RENDERMARKUP`UNDERLINE`` - Underline rendering
  - `%0` - The underlined content (already rendered)
  
- ``RENDERMARKUP`INLINECODE`` - Inline code (`` `text` ``) rendering
  - `%0` - The code text (plain text)
  
- ``RENDERMARKUP`LINK`` - Link rendering
  - `%0` - The link text as it would be shown (the URL, if the link has no text)
  - `%1` - The URL, or the command for a command link
  - `%2` - Is it a command link? (1 for a command, 0 for a URL)
  - `%3` - The link title/hint, empty if there is none
  
- ``RENDERMARKUP`WIKILINK`` - Wiki link (`[[Page Name]]`) rendering
  - `%0` - The display text
  - `%1` - The `@wiki` page reference, always fully qualified as
    `<namespace>:<category>:<slug>`
  - `%2` - The target page's title
  
- ``RENDERMARKUP`AUTOLINK`` - Autolink (`<https://example.com>`) rendering
  - `%0` - The URL, which is also the text shown
  
- ``RENDERMARKUP`IMAGE`` - Image (`![alt](url)`) rendering
  - `%0` - The alt text, empty if there is none
  - `%1` - The image URL
  
- ``RENDERMARKUP`TASKLIST`` - Task list marker (`- [x]`) rendering
  - `%0` - Is it ticked? (1 for `[x]`, 0 for `[ ]`)

**Note on TABLE's cells.** They are markdown *source*, not rendered text - a
cell reading `**loud**` arrives with its asterisks. Call `rendermarkdown()` on a
cell when you want it formatted; the choice of whether and how stays with you.
A literal pipe is written `\|` in table source and is handed over as written,
because `rendermarkdown()` is what resolves it.

Reach for `json_map()` rather than picking the JSON apart with string
functions: it walks `rows`, and then each row's own cells, so a per-row and a
per-cell helper compose. Two things about it are worth knowing before you write
one. It passes the callee `%0` type, `%1` the element's **raw JSON**, and `%2`
its index; a string element arrives quoted, so `json_query(%1,unescape)` is what
gets you the value. And the element lands in `%1`, not `%0`, which is why the
example below needs a one-token `ROWJ` adapter to hand a row on to `u()`.

Templates are evaluated with the *caller* as executor, so `me` inside one is the
player, not the object the templates live on. Address helper attributes by dbref.

**Note on `%2` for LINK.** SharpMUSH help text writes a bare `[topic]` for a
help-topic shortcut, and the renderer turns that into a link whose `%1` is the
command `help <topic>` rather than a URL. `%2` is how a template tells the two
apart - wrap `%2`-true links so the client runs them, and `%2`-false links so
the client opens them.

**Example Usage:**

Set up custom green color for headings:
```sharp
&RENDERMARKUP`H1 #123=[ansi(hg,%0)]
&RENDERMARKUP`H2 #123=[ansi(hc,%0)]
think rendermarkdowncustom(# My Heading, #123)
```
Output:
```markdown
My Heading
==========
```
(heading rendered in bright green with ANSI code hg)

Set up custom code block with yellow header:
```sharp
&RENDERMARKUP`CODEBLOCK #123=[ansi(hy,CODE:)]%r[ansi(h,%0)]
think rendermarkdowncustom(``````%rvar x = 42;%r```````, #123)
```
Output:
```
CODE:
  var x = 42;
```
(CODE: in bright yellow, code content in white)

Set up custom list items with colored bullets:
```sharp
&RENDERMARKUP`LISTITEM #123=[if(%0,ansi(hr,%1.) %2,ansi(hb,*) %2)]
think rendermarkdowncustom(- First%r- Second%r- Third, #123)
```
Output:
```markdown
* First
* Second
* Third
```
(bullets in bright blue via ANSI code hb)

Ordered list with custom template:
```sharp
&RENDERMARKUP`LISTITEM #123=[if(%0,ansi(hr,%1.) %2,ansi(hb,*) %2)]
think rendermarkdowncustom(1. First%r2. Second%r3. Third, #123)
```
Output:
```markdown
1. First
2. Second
3. Third
```
(numbers in bright red via ANSI code hr)

Complete example with multiple custom elements:
```sharp
&RENDERMARKUP`H1 #123=[ansi(hc,%0)]
&RENDERMARKUP`LISTITEM #123=[ansi(hg,->)] %2
&RENDERMARKUP`QUOTE #123=[ansi(hy,%0)]
think rendermarkdowncustom(# Tasks%r%r- Do this%r- Do that%r%r> Important note, #123)
```
Output:
```markdown
Tasks
=====

-> Do this
-> Do that

  Important note
```
(heading in cyan, arrow bullets in green, quote in yellow)

Spell out where a wiki link goes, instead of only styling it:
```sharp
&RENDERMARKUP`WIKILINK #123=[ansi(hc,%0)] %(@wiki %1%)
think rendermarkdowncustom(See %[%[Help:Markdown Guide%]%] first., #123)
```
Output:
```markdown
See Markdown Guide (@wiki help:general:markdown_guide) first.
```
`%1` arrives fully qualified, so the command it builds resolves whatever
namespace the link was written in. Under `@wiki` itself a wiki link is already
a clickable command link and needs no template; this hook is for the games that
want a different presentation of their own. (`%[`, `%]`, `%(` and `%)` are how
you get literal brackets and parentheses past the parser.)

A table template receives the table as JSON and lays it out itself. This one
rebuilds the default look with `lalign()`:
```sharp
&FUN`VAL #123=%1
&FUN`TABLE`SPEC #123=[json_query(%1,unescape)][json_query(%q<wd>,get,%2)]
&FUN`TABLE`CELL #123=[rendermarkdown([json_query(%1,unescape)],[max(10,json_query(%q<wd>,get,%2))])]
&FUN`TABLE`ROW #123=[lalign(%q<spec>,[json_map(#123/FUN`TABLE`CELL,%0,|)],|)]
&FUN`TABLE`ROWJ #123=[u(#123/FUN`TABLE`ROW,%1)]
&FUN`TABLE`HEAD #123=[ansi(hw,[u(#123/FUN`TABLE`ROW,[json_query(%0,get,head)])])]
&FUN`TABLE`RULE #123=[repeat(-,%q<tw>)]
&FUN`TABLE`BODY #123=[json_map(#123/FUN`TABLE`ROWJ,[json_query(%0,get,rows)],%r)]
&RENDERMARKUP`TABLE #123=[setq(wd,json_query(%0,get,widths))][setq(spec,json_map(#123/FUN`TABLE`SPEC,[json_query(%0,get,align)],%b))][setq(tw,add(lmath(add,[json_map(#123/FUN`VAL,%q<wd>,%b)]),sub(json_query(%q<wd>,size),1)))][jiter(#123/FUN`TABLE`HEAD #123/FUN`TABLE`RULE #123/FUN`TABLE`BODY,%0,%r)]
think rendermarkdowncustom(| Command | Effect | Cost |%r|:---|:-:|--:|%r| @wiki | Show the **index** | 0 |%r| @wiki/search | Find pages | 1 |%r| @wiki/audit | Staff report |, #123, 40)
```
Output:
```markdown
Command          Effect     Cost
--------------------------------
@wiki        Show the index    0
@wiki/search   Find pages      1
@wiki/audit   Staff report
```
(header in bright white, `index` bold because the cell was passed through
`rendermarkdown()`, and the short last row padded out by `lalign()`)

Three registers are computed once from the payload: `%q<wd>` keeps `widths` as
JSON for indexed lookup, `%q<spec>` zips `align` with `widths` into an `align()`
width spec, and `%q<tw>` is the table's own width. `jiter()` then fans the
payload across the three bands, each receiving it as `%0`.

`json_map()` supplies the element value as `%1` and its index as `%2` - the
index is how `SPEC` and `CELL` find their own column's width, and
`FUN`TABLE`ROWJ` exists only to move that value into `%0` for `u()`. A string
element arrives as raw JSON, quoted, which is why `SPEC` and `CELL` read it
through `json_query(%1,unescape)`. `FUN`TABLE`ROW` and `FUN`TABLE`CELL` read
`%q<spec>` and `%q<wd>` from the enclosing call, so a `ulocal()` in this chain
would break them. The `max(10,...)` is `rendermarkdown()`'s own minimum width,
which a narrow column can fall under.

**Elements with no template.** Every element the renderer can be asked to style
is in the list above. What is left renders with fixed behaviour and has no hook:
plain text, paragraphs, line breaks, horizontal rules, raw HTML, and the
individual rows and cells of a table, which arrive as part of `TABLE` rather
than as hooks of their own.

**Notes:**
- If a template attribute is not found on the object, the default rendering is used
- Templates are evaluated as softcode with the element content passed as arguments
- Custom templates receive already-rendered content for some elements (headings, quotes)
- List items receive metadata (%0 for ordered/unordered, %1 for index)
- All error handling follows standard MUSH patterns
- Falls back gracefully to default rendering if template evaluation fails

**Error Handling:**
- Returns `#-1 INVALID WIDTH (must be 10-1000)` if width parameter is out of range
- Returns `#-1 <locate error>` if template object cannot be found
- Returns `#-1 ERROR RENDERING MARKDOWN: <error>` if markdown parsing fails
- Falls back to default rendering if template attribute evaluation fails

**See Also:**
- [rendermarkdown()]
- [get()]
- [u()]
- [ANSI]