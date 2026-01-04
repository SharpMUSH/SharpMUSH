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

**STATUS: PLANNED FOR FUTURE IMPLEMENTATION**

Renders CommonMark/Markdown text with customizable rendering controlled by attributes on the specified object. This will allow you to define custom ANSI styles, colors, and formatting for each markdown element type.

**Parameters:**
- `<markdown>` - The markdown/CommonMark text to render
- `<object>` - Object reference (dbref) containing rendering template attributes
- `<width>` - Optional. Maximum width for rendered output (default: 78). Must be between 10-1000.

**Planned Architecture:**

The function will look for attributes on `<object>` with specific names that define how to render each markdown element. These attributes will contain softcode that is evaluated with markdown content passed as arguments.

**Template Attribute Names (Planned):**

- `RENDERMARKUP`BOLD` - Bold text rendering
- `RENDERMARKUP`ITALIC` - Italic text rendering  
- `RENDERMARKUP`CODE` - Inline code rendering
- `RENDERMARKUP`H1` - Heading level 1 rendering
- `RENDERMARKUP`H2` - Heading level 2 rendering
- `RENDERMARKUP`H3` - Heading level 3 rendering
- `RENDERMARKUP`LINK` - Link rendering
- `RENDERMARKUP`AUTOLINK` - Autolink rendering
- `RENDERMARKUP`LIST`BULLET` - Unordered list bullet rendering
- `RENDERMARKUP`LIST`NUMBER` - Ordered list number rendering
- `RENDERMARKUP`QUOTE` - Block quote rendering
- `RENDERMARKUP`TABLE`BORDER` - Table border rendering
- `RENDERMARKUP`TABLE`SEPARATOR` - Table separator rendering
- `RENDERMARKUP`TABLE`CELL` - Table cell rendering
- `RENDERMARKUP`CODEBLOCK` - Code block rendering

**Example Planned Usage:**

Set up custom green color for bold text:
```
&RENDERMARKUP`BOLD #123=[ansi(hg,%0)]
```

Set up custom red headings:
```
&RENDERMARKUP`H1 #123=[ansi(hr,%0)]
&RENDERMARKUP`H2 #123=[ansi(hr,%0)]
```

Set up custom link display with cyan color:
```
&RENDERMARKUP`LINK #123=[ansi(hc,%0)] %(%1%)
```

Use custom rendering:
```
think rendermarkdowncustom(This is **bold** text, #123)
```

**Implementation Note:**

This function is planned for future implementation. The architecture for custom rendering is established in the RecursiveMarkdownRenderer class through virtual methods (RenderBold(), RenderItalic(), etc.), which can be overridden by a custom renderer class that evaluates object attributes as templates.

**Current Workaround:**

Until this function is implemented, you can use the base `rendermarkdown()` function and post-process the output with custom ANSI codes using standard MUSH softcode functions.

## See Also
- [rendermarkdown()]
- [get()]
- [u()]
- [ANSI]
