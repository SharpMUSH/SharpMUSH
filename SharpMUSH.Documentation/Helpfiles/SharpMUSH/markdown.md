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
Output: "This is **bold** and *italic* text" (with ANSI formatting)

Headings:
```
think rendermarkdown(# My Heading%r%rThis is a paragraph)
```

Links (note the escaping):
```
think rendermarkdown(%[Click here%]%(https://example.com%))
```
Output: Clickable "Click here" hyperlink

Tables:
```
think rendermarkdown(| Name | Age |%r|------|-----|%r| Alice | 30 |%r| Bob | 25 |)
```
Output: Formatted ASCII table with borders

Tables with custom width:
```
think rendermarkdown(| Name | Age |%r|------|-----|%r| Alice | 30 |, 50)
```
Output: Table constrained to 50 characters width

Code blocks:
```
think rendermarkdown(``````%rvar x = 42;%rvar y = 100;%r```````)
```
Output: Code block with 2-space indentation

Lists:
```
think rendermarkdown(1. First item%r2. Second item%r3. Third item)
```
Output: Numbered list with ANSI-styled bullets

**Error Handling:**
- Returns `#-1 INVALID WIDTH (must be 10-1000)` if width parameter is out of range
- Returns `#-1 ERROR RENDERING MARKDOWN: <error>` if markdown parsing fails

**Notes:**
- Tables automatically expand to use full available width for professional spacing
- Links use ANSI OSC 8 hyperlinks, making them clickable in compatible terminals (iTerm2, Windows Terminal, etc.)
- All structural elements (borders, bullets) use ANSI faint/dim styling for visual distinction
- Output is proper MarkupString with embedded ANSI codes

## See Also
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
```

Set up custom code block with background:
```
&RENDERMARKUP`CODEBLOCK #123=[ansi(hy,CODE:)]%r[ansi(h,%0)]
```

Set up custom list items with different bullets:
```
&RENDERMARKUP`LISTITEM #123=[if(%0,ansi(hr,[add(%1,1)].) %2,ansi(hb,*) %2)]
```

Use custom rendering:
```
think rendermarkdowncustom(**Bold** and *italic*, #123)
think rendermarkdowncustom(# My Heading%r%rParagraph text, #123)
think rendermarkdowncustom(1. First%r2. Second, #123, 60)
```

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

## See Also
- [rendermarkdown()]
- [get()]
- [u()]
- [ANSI]
