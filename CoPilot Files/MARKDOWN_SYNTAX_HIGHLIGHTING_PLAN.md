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

4. **MUSH code blocks** (`Info == "mush"` or `"mushcode"`) — instead of a regex-based
   ColorCode language, use `IMUSHCodeParser.GetSemanticTokens()` directly (see
   _MUSH-specific language support_ section below). This bypasses ColorCode entirely
   for MUSH code and reuses the same semantic classification already in use by the
   language server.

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
convention). The language server already provides everything needed for high-fidelity
MUSH highlighting — this should be the primary approach.

### Recommended: Use `IMUSHCodeParser.GetSemanticTokens()` directly

The `IMUSHCodeParser` interface (in `SharpMUSH.Library`) exposes two methods that are
already used by the language server (`SemanticTokensHandler`):

```csharp
// High-fidelity semantic tokens (same API the LSP uses)
IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType)

// Lower-level syntactic tokens from the ANTLR lexer
IReadOnlyList<TokenInfo> Tokenize(MString text)
```

Each `SemanticToken` carries:
- `SemanticTokenType` — covers all MUSH constructs: `Function`, `UserFunction`,
  `ObjectReference`, `Substitution`, `Register`, `Command`, `BracketSubstitution`,
  `BraceGroup`, `EscapeSequence`, `AnsiCode`, `Operator`, `Number`, `Text`, etc.
- `SemanticTokenModifier` — e.g. `DefaultLibrary` distinguishes built-in functions
  from user-defined ones.
- `Range` — start/end positions for each token.

**Integration pattern for terminal rendering:**

```csharp
// In RenderCodeBlock (RecursiveMarkdownRenderer.cs):
if (code is FencedCodeBlock fenced &&
    (fenced.Info?.Equals("mush", StringComparison.OrdinalIgnoreCase) == true ||
     fenced.Info?.Equals("mushcode", StringComparison.OrdinalIgnoreCase) == true) &&
    _mushParser != null)
{
    // Use the semantic token pipeline (same pipeline as the language server)
    var sourceText = string.Join("\n",
        code.Lines.Lines
            ?.Where(l => l.Slice.Text != null)
            .Select(l => l.Slice.ToString()) ?? []);
    var tokens = _mushParser.GetSemanticTokens(MModule.single(sourceText));
    return RenderMushHighlighted(tokens, sourceText);
}
```

The `SemanticTokenType` → ANSI colour mapping is a single new class
(`SemanticTokenAnsiPalette`) that can also be referenced by any future tooling:

```csharp
// Suggested palette (aligned with VS Code dark theme conventions)
Function         → #DCDCAA  (yellow)
UserFunction     → #4EC9B0  (teal)
ObjectReference  → #9CDCFE  (light blue)
Substitution     → #4FC1FF  (bright blue)
Register         → #9CDCFE  (light blue)
Command          → #C586C0  (purple)  
BracketSubstitution → #569CD6 (blue bracket)
EscapeSequence   → #D7BA7D  (orange)
AnsiCode         → #808080  (grey, dimmed)
Operator         → #D4D4D4  (light grey)
Number           → #B5CEA8  (light green)
Text             → (default foreground, no colour override)
```

**Dependency path:**
- `SharpMUSH.Documentation` references `SharpMUSH.MarkupString` (already exists).
- Add a project reference to `SharpMUSH.Library` (lightweight — contains only models
  and interfaces, no runtime/DI services).
- `RecursiveMarkdownRenderer` accepts an optional `IMUSHCodeParser` parameter; when
  `null` (the library is used standalone without the parser), MUSH code blocks fall
  back to plain text.
- `MarkdownFunctions.cs` in `SharpMUSH.Implementation` already has access to
  `IMUSHCodeParser` via DI and can pass it through to the renderer.

**Advantages over regex-based approaches:**
- Already tested and used in production by the language server.
- Understands MUSH semantics (built-in vs user functions, dbref formats, register
  syntax, etc.).
- No ReDoS risk — uses the same ANTLR4 lexer already in the pipeline.
- `SemanticTokenType` enum is already defined in `SharpMUSH.Library.Models` — no
  duplication required.

### Blazor wiki client (HTML path)

The Blazor client does not have access to the MUSH parser at render time (it is a
WebAssembly app). For MUSH code blocks in the wiki, two options are available:

1. **Server-side pre-rendering** — before returning the wiki page, run the MUSH
   semantic tokenizer and convert to highlighted HTML server-side, then serve the
   pre-coloured HTML to the client.

2. **Custom Markdig extension** — add a server-side Markdig extension that intercepts
   `FencedCodeBlock` with `Info == "mush"` and emits inline-styled HTML using
   `IMUSHCodeParser.GetSemanticTokens()`, similar to how `Markdown.ColorCode` works
   for other languages. This is the cleanest long-term approach and reuses the
   same palette as the terminal renderer.

### Legacy Option — Custom ColorCode language (not recommended for MUSH)

A regex-based `ColorCode.ILanguage` was originally considered for MUSH support.
This approach is **not recommended** because the existing semantic token infrastructure
provides far higher accuracy without additional maintenance burden. However, it remains
a valid fallback if introducing a `SharpMUSH.Library` reference to the Documentation
project is undesirable.

---

## File Change Summary (when implementing)

| File | Change |
|---|---|
| `SharpMUSH.Documentation/SharpMUSH.Documentation.csproj` | Add `ColorCode.Core` 2.0.15; add project ref to `SharpMUSH.Library` |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/AnsiStyleSheet.cs` | New — ANSI colour mapping for ColorCode tokens (standard languages) |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/SemanticTokenAnsiPalette.cs` | New — `SemanticTokenType` → ANSI colour mapping for MUSH code blocks |
| `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.cs` | Modify `RenderCodeBlock`: detect language, route to ColorCode or MUSH semantic tokenizer; accept optional `IMUSHCodeParser` |
| `SharpMUSH.Implementation/Functions/MarkdownFunctions.cs` | Pass `IMUSHCodeParser` (available via DI) into the renderer |
| `SharpMUSH.Client/SharpMUSH.Client.csproj` | Add `Markdown.ColorCode` 3.0.1 |
| `SharpMUSH.Client/Components/WikiDisplay.razor` | Update Markdig pipeline with `.AddColorCode()` for standard languages; add custom MUSH extension |

---

## Security Considerations

- `ColorCode.Core` operates entirely on in-memory strings; it makes no network calls
  and poses no SSRF or injection risk.
- Language detection relies on the user-supplied fenced code block tag (`Info` field).
  ColorCode's `FindById` returns `null` for unknown identifiers; the code must fall
  back safely.
- MUSH code highlighting uses `IMUSHCodeParser.GetSemanticTokens()` which drives the
  ANTLR4 lexer — no ReDoS risk and already tested by the language server test suite.

---

## References

- [ColorCode-Universal (GitHub)](https://github.com/CommunityToolkit/ColorCode-Universal)
- [Markdown.ColorCode (GitHub)](https://github.com/wbaldoumas/markdown-colorcode)
- [TextMateSharp (GitHub)](https://github.com/danipen/TextMateSharp)
- [Markdig pipeline extensions](https://github.com/xoofx/markdig/blob/master/src/Markdig/MarkdownExtensions.cs)
- [SharpMUSH parser syntax highlighting](./PARSER_ERROR_SYNTAX_HIGHLIGHTING.md)
- [SharpMUSH LSP semantic highlighting](./LSP_SEMANTIC_HIGHLIGHTING.md)
- [SharpMUSH LSP implementation summary](./LSP_IMPLEMENTATION_SUMMARY.md)
- `SharpMUSH.Library/Models/SemanticTokenType.cs` — `SemanticTokenType` / `SemanticTokenModifier` enums
- `SharpMUSH.Implementation/MUSHCodeParser.cs` — `GetSemanticTokens()` / `Tokenize()` implementations
- `SharpMUSH.LanguageServer/Handlers/SemanticTokensHandler.cs` — reference usage of `GetSemanticTokens()`
