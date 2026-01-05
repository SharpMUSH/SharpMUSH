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
- **Code Blocks**: Triple-backtick code blocks with 2-space indentation
- **Block Quotes**: `> Quote` rendered with 2-space indentation
- **HTML Entities**: `&amp;`, `&lt;`, etc.

**MUSH Special Character Escaping:**
When using markdown features with square brackets `[` `]` or parentheses `(` `)`, you must escape them using `%`:
- `%[` for `[`
- `%]` for `]`
- `%(` for `(`
- `%)` for `)`

**Examples:**

Basic text formatting:
```
think rendermarkdown(This is **bold** and *italic* text)
```
Output:
```
This is bold and italic text
```
(with ANSI codes for bold and italic styling)

Headings:
```
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
```
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
```
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
```
| Name              | Age               |
|-------------------|-------------------|
| Alice             | 30                |
| Bob               | 25                |
```
(table fits within 50 character width)

Code blocks:
```
think rendermarkdown(``````%rvar x = 42;%rvar y = 100;%r```````)
```
Output:
```
  var x = 42;
  var y = 100;
```
(2-space indentation on all code lines)

Ordered lists:
```
think rendermarkdown(1. First item%r2. Second item%r3. Third item)
```
Output:
```
1. First item
2. Second item
3. Third item
```
(numbers styled with ANSI faint)

Unordered lists:
```
think rendermarkdown(- First item%r- Second item%r- Third item)
```
Output:
```
- First item
- Second item
- Third item
```
(bullets styled with ANSI faint)

Block quotes:
```
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

**Supported Template Attributes:**

- `RENDERMARKUP`H1` - Heading level 1 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- `RENDERMARKUP`H2` - Heading level 2 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- `RENDERMARKUP`H3` - Heading level 3 rendering
  - `%0` - The heading content (already formatted with base styles)
  
- `RENDERMARKUP`CODEBLOCK` - Code block rendering
  - `%0` - The code content (plain text, newlines separated)
  
- `RENDERMARKUP`LISTITEM` - List item rendering
  - `%0` - Is ordered list? (1 for ordered, 0 for unordered)
  - `%1` - Item index (0-based)
  - `%2` - The list item content
  
- `RENDERMARKUP`QUOTE` - Block quote rendering
  - `%0` - The quote content (already rendered)

**Example Usage:**

Set up custom green color for headings:
```
&RENDERMARKUP`H1 #123=[ansi(hg,%0)]
&RENDERMARKUP`H2 #123=[ansi(hc,%0)]
think rendermarkdowncustom(# My Heading, #123)
```
Output:
```
My Heading
==========
```
(heading rendered in bright green with ANSI code hg)

Set up custom code block with yellow header:
```
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
```
&RENDERMARKUP`LISTITEM #123=[if(%0,ansi(hr,[add(%1,1)].) %2,ansi(hb,*) %2)]
think rendermarkdowncustom(- First%r- Second%r- Third, #123)
```
Output:
```
* First
* Second
* Third
```
(bullets in bright blue via ANSI code hb)

Ordered list with custom template:
```
&RENDERMARKUP`LISTITEM #123=[if(%0,ansi(hr,[add(%1,1)].) %2,ansi(hb,*) %2)]
think rendermarkdowncustom(1. First%r2. Second%r3. Third, #123)
```
Output:
```
1. First
2. Second
3. Third
```
(numbers in bright red via ANSI code hr)

Complete example with multiple custom elements:
```
&RENDERMARKUP`H1 #123=[ansi(hc,%0)]
&RENDERMARKUP`LISTITEM #123=[ansi(hg,→)] %2
&RENDERMARKUP`QUOTE #123=[ansi(hy,%0)]
think rendermarkdowncustom(# Tasks%r%r- Do this%r- Do that%r%r> Important note, #123)
```
Output:
```
Tasks
=====

→ Do this
→ Do that

  Important note
```
(heading in cyan, arrow bullets in green, quote in yellow)

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
