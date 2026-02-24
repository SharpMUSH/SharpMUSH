# Syntax Highlighting for Markdown Render Functions

## Overview

This document outlines the plan for adding syntax highlighting to code blocks rendered
by SharpMUSH's Markdown rendering pipeline. The goal is to enrich the output of
`RENDERMARKDOWN()` and `RENDERMARKDOWNCUSTOM()` functions so that fenced code blocks
with a language tag produce ANSI-coloured output rather than plain monospaced text.

A secondary goal is to enhance the Blazor wiki client so that its HTML output also
benefits from syntax-coloured code blocks.

---

## Current State

### ANSI Terminal Renderer (`SharpMUSH.Documentation`)

The rendering pipeline is:

```
RENDERMARKDOWN(text) / RENDERMARKDOWNCUSTOM(text, obj)
    ↓
RecursiveMarkdownHelper.RenderMarkdown()
    ↓
Markdig.Markdown.Parse() → MarkdownDocument AST
    ↓
RecursiveMarkdownRenderer.Render()
    ↓
RenderCodeBlock(CodeBlock code)   ← plain text, no highlighting
    ↓
MString with ANSI escape codes → player terminal
```

`RenderCodeBlock` currently renders every code block as plain indented text:

```csharp
protected virtual MString RenderCodeBlock(CodeBlock code)
{
    var lines = code.Lines.Lines?
        .Where(line => line.Slice.Text != null)
        .Select(line => MModule.single("  " + line.Slice.ToString()))
        .ToList() ?? new List<MString>();

    return MModule.multipleWithDelimiter(MModule.single("\n"), lines);
}
```

A fenced code block such as:

````markdown
```python
def hello():
    print("hi")
```
````

is parsed by Markdig into a `FencedCodeBlock` (a subtype of `CodeBlock`).
The `FencedCodeBlock.Info` property holds the language identifier (`"python"`).
This information is currently discarded.

### Blazor Web Client (`SharpMUSH.Client`)

The wiki display (`WikiDisplay.razor`) calls `Markdig.Markdown.ToHtml()` and injects
the HTML string into a Blazor `MarkupString`. No syntax highlighting is applied at the
server side; any highlighting would need either a Markdig pipeline extension or a
client-side JavaScript library (e.g., Prism.js, Highlight.js).

---

## Architecture Constraints

| Constraint | Detail |
|---|---|
| Target framework | .NET 10 |
| Markdig version | 1.0.0 |
| Output for terminal | ANSI 24-bit RGB via `ANSILibrary` F# module |
| Output for browser | HTML string via `Markdig.Markdown.ToHtml()` |
| MUSH language tag | Custom language `mush` / `mushcode` not in any third-party library |
| Existing ANSI API | `Ansi.Create(foreground: StringExtensions.rgb(Color))` accepts `System.Drawing.Color` |

---

## NuGet Package Evaluation

### 1. `ColorCode.Core` ★ Recommended for terminal rendering

| | |
|---|---|
| **NuGet ID** | `ColorCode.Core` |
| **Latest** | 2.0.15 |
| **Owner** | Microsoft / CommunityToolkit |
| **Downloads** | ~2.4 million |
| **Licence** | MIT |
| **GitHub** | https://github.com/CommunityToolkit/ColorCode-Universal |

**What it provides:**
- Language-aware tokenization for C#, C/C++, Java, JavaScript, TypeScript, HTML, CSS,
  SQL, Python, PowerShell, Ruby, PHP, F#, VB, and more.
- An `IStyleSheet` abstraction that maps token types (keyword, string, comment, etc.)
  to CSS-style colour values.
- A `CodeColorizer` that yields segments: `(text, foregroundColour, backgroundColour)`.

**How it maps to our pipeline:**
- `FencedCodeBlock.Info` → look up `ILanguage` via `ColorCode.Languages.FindById()`.
- If a matching language is found, call `CodeColorizer.Colorize()` to get coloured
  segments.
- Each segment carries a CSS hex colour (e.g., `#569CD6`). Convert with
  `System.Drawing.ColorTranslator.FromHtml()` → `System.Drawing.Color` →
  `StringExtensions.rgb(color)` → `Ansi.Create(foreground: ...)`.
- Wrap each segment in a `MModule.markupSingle(ansi, text)` and concatenate.

**Pros:**
- Clean programmatic API — does not force HTML output.
- CSS colour values map directly to our existing 24-bit ANSI infrastructure.
- Actively maintained; the same tokenizer is used by Windows documentation tooling.
- Easy to register a **custom language** (see MUSH section below).

**Cons:**
- Designed around an `IStyleSheet` / CSS model; ANSI mapping requires a thin adapter.
- No built-in MUSH / MUSHcode language.

---

### 2. `Markdown.ColorCode` — for Blazor HTML output

| | |
|---|---|
| **NuGet ID** | `Markdown.ColorCode` |
| **Latest** | 3.0.1 |
| **Owner** | wbaldoumas |
| **Downloads** | ~187,000 |
| **Licence** | MIT |
| **GitHub** | https://github.com/wbaldoumas/markdown-colorcode |

This is the successor to `Markdig.SyntaxHighlighting`. It is a Markdig **pipeline
extension** built on top of `ColorCode.Core` that replaces fenced code blocks with
HTML `<code>` elements containing `<span style="color:…">` segments. It is the natural
fit for the Blazor wiki client.

**Integration (Blazor client):**

```csharp
// WikiDisplay.razor or a shared Markdown service
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .AddColorCode()          // ← Markdown.ColorCode extension
    .Build();

var html = Markdown.ToHtml(markdownText, pipeline);
```

The resulting HTML is ready to embed directly in the Blazor `MarkupString`; no
additional CSS is needed because colours are applied via inline styles.

**Pros:**
- Zero-configuration Markdig pipeline extension.
- Reuses `ColorCode.Core` so language coverage is identical.
- Inline styles mean no external CSS file required.

**Cons:**
- HTML output only — cannot be used for terminal ANSI rendering.
- Does not support custom language definitions out of the box (but ColorCode.Core
  custom language registration applies globally, so it would propagate).

---

### 3. `TextMateSharp` + `TextMateSharp.Grammars` — high-accuracy alternative

| | |
|---|---|
| **NuGet IDs** | `TextMateSharp` 2.0.3 + `TextMateSharp.Grammars` 2.0.3 |
| **Owner** | danipen |
| **Downloads** | ~665,000 + ~555,000 |
| **Licence** | MIT |
| **GitHub** | https://github.com/danipen/TextMateSharp |

TextMateSharp is a C# port of the VS Code TextMate grammar engine. It produces
scope-annotated tokens (e.g., `keyword.control.python`, `string.quoted.double`) that
map to theme colours.

**Pros:**
- VSCode-quality accuracy; covers the widest language set.
- Theme files can be customised to fit a MUSH colour palette.

**Cons:**
- Significantly heavier dependency (grammar JSON files, theme files).
- More complex setup (registry, grammar loading, theme application).
- MUSH language would require writing a complete TextMate grammar file.
- Overkill for the help-file use case.

**Verdict:** Not recommended for the initial implementation; may be revisited if
ColorCode.Core coverage proves insufficient for specific languages used in SharpMUSH
help files.

---

### 4. Ruled-out options

| Package | Reason |
|---|---|
| `Markdig.SyntaxHighlighting` (1.1.7) | Superseded by `Markdown.ColorCode` 3.x |
| `MarkdigExtensions.SyntaxHighlighting` (1.0.3) | Low activity, also HTML-only |
| `WebStoating.Markdig.Prism` | Requires Prism.js runtime in browser |
| `ColorCode.HTML` | HTML-only renderer on top of ColorCode.Core; use ColorCode.Core directly |

---

## Recommended Implementation Plan

### Phase 1 — Terminal ANSI highlighting (`SharpMUSH.Documentation`)

1. **Add `ColorCode.Core` 2.0.15** to `SharpMUSH.Documentation.csproj`.

2. **Create `AnsiStyleSheet`** — a class implementing `IStyleSheet` (or inheriting from
   `DefaultStyleSheet`) that maps `ColorCode.ScopeName` values to named ANSI colour
   constants. This drives what colour each token type receives in the terminal.

   ```csharp
   // Suggested palette aligned with common dark-terminal defaults
   // keyword → #569CD6 (blue)
   // string  → #CE9178 (orange)
   // comment → #6A9955 (green)
   // number  → #B5CEA8 (light green)
   // etc.
   ```

3. **Modify `RenderCodeBlock`** in `RecursiveMarkdownRenderer`:
   - Cast to `FencedCodeBlock`; if cast fails (indented code block), fall back to
     current plain-text rendering.
   - Call `ColorCode.Languages.FindById(fencedBlock.Info)`.
   - If a language is found, use `CodeColorizer` + `AnsiStyleSheet` to get coloured
     `MString` segments; otherwise fall back to plain text.

4. **Register a MUSH language** using `ColorCode.ILanguage` / regex-based rules for
   common MUSH constructs (`[function()]`, `%substitutions`, `@commands`, etc.).
   This is optional but valuable since help files frequently contain MUSH code examples.
   Alternatively, the existing `IMUSHCodeParser.Tokenize()` method can be used to
   tokenise MUSH code blocks, bypassing ColorCode entirely.

### Phase 2 — Blazor HTML highlighting (`SharpMUSH.Client`)

1. **Add `Markdown.ColorCode` 3.0.1** to `SharpMUSH.Client.csproj`.

2. **Update the Markdig pipeline** in `WikiDisplay.razor` (or a shared wiki service)
   to include `.AddColorCode()`.

3. **Optional CSS class mode**: `Markdown.ColorCode` supports a CSS-class mode
   (`SyntaxHighlightingTheme`) if inline styles are undesirable; a bundled stylesheet
   can then define the colours.

---

## MUSH-Specific Language Support

SharpMUSH help files use fenced code blocks tagged as `mush` or `mushcode` (suggested
convention). Two approaches are viable:

### Option A — Custom ColorCode language (simpler)

Register a `ColorCode.ILanguage` instance with regex rules matching:
- Function calls: `\w+\(` → Keyword colour
- Bracket substitutions: `\[…\]` → Special colour
- Percent substitutions: `%[0-9a-zA-Z#!@?]+` or `%q<\w+>` → Variable colour  
- ANSI codes: `\e\[…m` → Comment colour (dimmed)
- `@commands` → Command colour

This integrates transparently into both the ANSI and HTML pipelines.

### Option B — Use existing ANTLR tokenizer (higher fidelity)

The `IMUSHCodeParser.Tokenize()` method already produces `TokenInfo` records with
`Type`, `Text`, `StartIndex`, and `EndIndex`. A specialised branch in
`RenderCodeBlock` can detect `Info == "mush"` and call the MUSH parser directly,
mapping ANTLR token types to ANSI colours using the mapping from
`PARSER_ERROR_SYNTAX_HIGHLIGHTING.md`.

This option produces the most accurate MUSH highlighting but requires a dependency
on the parser from the Documentation project. Given the current project structure,
Option A is preferred for loose coupling.

---

## File Change Summary (when implementing)

| File | Change |
|---|---|
| `SharpMUSH.Documentation/SharpMUSH.Documentation.csproj` | Add `ColorCode.Core` 2.0.15 |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/AnsiStyleSheet.cs` | New — ANSI colour mapping for ColorCode tokens |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/MushLanguage.cs` | New — Custom ColorCode language for MUSH code |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.cs` | Modify `RenderCodeBlock` to use ColorCode |
| `SharpMUSH.Client/SharpMUSH.Client.csproj` | Add `Markdown.ColorCode` 3.0.1 |
| `SharpMUSH.Client/Components/WikiDisplay.razor` | Update Markdig pipeline with `.AddColorCode()` |

---

## Security Considerations

- `ColorCode.Core` operates entirely on in-memory strings; it makes no network calls
  and poses no SSRF or injection risk.
- Language detection relies on the user-supplied fenced code block tag (`Info` field).
  ColorCode's `FindById` returns `null` for unknown identifiers; the code must fall
  back safely.
- Custom MUSH language regex patterns should be reviewed to avoid ReDoS. Use
  possessive quantifiers or fixed-length patterns where possible.

---

## References

- [ColorCode-Universal (GitHub)](https://github.com/CommunityToolkit/ColorCode-Universal)
- [Markdown.ColorCode (GitHub)](https://github.com/wbaldoumas/markdown-colorcode)
- [TextMateSharp (GitHub)](https://github.com/danipen/TextMateSharp)
- [Markdig pipeline extensions](https://github.com/xoofx/markdig/blob/master/src/Markdig/MarkdownExtensions.cs)
- [SharpMUSH parser syntax highlighting](./PARSER_ERROR_SYNTAX_HIGHLIGHTING.md)
- [SharpMUSH LSP semantic highlighting](./LSP_SEMANTIC_HIGHLIGHTING.md)
