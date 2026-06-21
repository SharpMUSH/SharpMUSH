# Terminal Command Links vs. Navigation Links — Design

**Date:** 2026-06-20
**Branch:** `feature/terminal-command-links`
**Status:** Approved design, pending spec review

## Problem

In the SharpMUSH.Client WebSocket terminal (Blazor WASM), markdown links render as
real navigation links (`<a href="…">`) regardless of what they represent. The `help`
command's markdown contains bare `[topic]` shortcuts that are converted to a *command*
(`help <topic>`), not a URL — yet they render as `<a href="help topic">`, so clicking
makes the browser try to navigate to a bogus route instead of running the command.

These command links are conceptually distinct from ordinary `<https://…>` navigation
links, but the markup layer flattens both into a single ambiguous `LinkUrl` string and
the HTML renderer emits `<a href>` for everything.

The canonical convention (documented in
`SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharppueb.md`, lines 189–206, matching the
grapenut `websockclient`) is:

- **Command link:** `<a xch_cmd="+who">Who is online?</a>` (+ optional `xch_hint` tooltip)
- **Navigation link:** `<a href="url">text</a>`

MXP (`Markup.cs` `WrapAsMxp`) uses `<SEND HREF="cmd">` for commands vs `<A HREF="url">`
for navigation; Pueblo uses `XCH_CMD` vs `HREF`.

## Root Cause

`AnsiStructure.LinkUrl` (`SharpMUSH.MarkupString/Markup/Markup.cs:20`) is a single string
with no type information. The distinction between "command" and "URL" exists only
implicitly at *production* time (the `HelpTopicInlineParser` knows it built a command;
ordinary links know they carry a URL) and is lost before *render* time.

## Design Decisions

1. **Explicit link kind in the markup** (not a render-time URL heuristic). The producer
   declares intent; renderers act on it. Chosen for correctness and future-proofing.
2. **Fix all output formats consistently** in one pass, since `LinkKind` lives in the
   shared markup layer used by HTML (WASM), Pueblo, MXP, BBCode, and ANSI/OSC8.
3. **Preserve the hint/tooltip path** (`xch_hint` / MXP `HINT` / HTML `title`).

## Architecture

### 1. Data model — `SharpMUSH.MarkupString/Markup/Markup.cs`

- New enum:

  ```csharp
  public enum LinkKind { Url = 0, Command = 1 }
  ```

  `Url` is value `0` and the default, so existing serialized markup (which has no field)
  deserializes to `Url` and keeps today's navigation behavior for ordinary links.

- Add to `AnsiStructure`:

  ```csharp
  public LinkKind LinkKind { get; init; }
  ```

- Add a `linkKind` parameter (default `LinkKind.Url`) to `AnsiMarkup.Create(...)`.

- `LinkText` (already present on `AnsiStructure`, currently set but never read) becomes the
  **hint**: rendered as `xch_hint` (Pueblo), `HINT` (MXP), and `title` (HTML) when non-empty.

**Serialization:** `AnsiStructure` is serialized via `System.Text.Json` in
`MarkupStringModule`. The new property round-trips automatically. A round-trip test plus a
legacy-payload (no `LinkKind` field → `Url`) test lock this down.

### 2. Renderers — same file, switched on `LinkKind`

`LinkUrl` empty → no link wrapper (unchanged). When `LinkUrl` is non-empty:

| Format | `Command` | `Url` |
|---|---|---|
| HTML (`WrapAsHtmlClass`) | `<a class="ms-cmd-link" xch_cmd="{enc}"[ title="{hint}"]>` | `<a href="{enc}" target="_blank" rel="noopener noreferrer">` |
| Pueblo (`WrapAsPueblo`) | `<A XCH_CMD="{enc}"[ XCH_HINT="{hint}"]>` | `<A HREF="{enc}">` |
| MXP (`WrapAsMxp`) | `<SEND HREF="{enc}"[ HINT="{hint}"]>` | `<A HREF="{enc}">` |
| BBCode (`WrapAsBBCode`) | plain text (no command concept) | `[url={…}]` |
| ANSI/OSC8 (`ApplyDetails`) | plain text (OSC8 cannot run a command) | OSC8 link (unchanged) |

All attribute values are HTML-encoded with `WebUtility.HtmlEncode` (as today). The hint
attribute is emitted only when `LinkText` is non-empty.

### 3. Markdown producer — `SharpMUSH.Documentation`

- `HelpTopicInlineParser.Match` tags the `LinkInline` it builds:
  `link.SetData(HelpTopicLink.CommandMarkerKey, true)` (a shared constant key). The URL it
  already sets (`"help " + topic`) becomes the command string.
- `RecursiveMarkdownRenderer.RenderLink`:
  - reads the marker: `link.GetData(HelpTopicLink.CommandMarkerKey) is true` → `LinkKind.Command`, else `LinkKind.Url`;
  - maps `link.Title` (markdown `[text](url "title")`) → `linkText` (hint) when non-empty;
  - calls `Ansi.Create(linkUrl: url, linkKind: kind, linkText: hint)`.
- `RecursiveMarkdownRenderer.RenderAutolink` is always `LinkKind.Url`.

Result: `help` output renders `[ATTRIBUTES]` as a `help ATTRIBUTES` command link, while
`[SharpMUSH](https://sharpmush.com)` stays a real navigation link that opens in a new tab.

### 4. Client — `SharpMUSH.Client`

Terminal lines are injected as raw HTML via `@((MarkupString)line.Html)`
(`GlobalTerminal.razor:20`), so Blazor cannot bind `@onclick` to those anchors. Use JS
event delegation bridged to .NET:

- `wwwroot/js/mush-monaco.js`: add to `SharpMUSH.Terminal`:
  - `attachCommandLinks(outputId, dotNetRef)` — registers a single delegated `click`
    listener on the output container. On click, walk up from `event.target` to find an
    ancestor `a[xch_cmd]`; if found, `preventDefault()` and
    `dotNetRef.invokeMethodAsync('RunCommandLinkAsync', el.getAttribute('xch_cmd'))`.
    Plain `a[href]` links are left alone (default new-tab navigation via the rendered
    `target="_blank"`).
  - `detachCommandLinks(outputId)` — removes the listener (idempotent).
- `GlobalTerminal.razor`:
  - hold a `DotNetObjectReference<GlobalTerminal>`;
  - in `OnAfterRenderAsync(firstRender)` call `attachCommandLinks(_outputId, _selfRef)`;
  - add `[JSInvokable] public Task RunCommandLinkAsync(string command)` →
    `await Terminal.SendAsync(command)` (identical to the user typing the command, so echo
    and history behave the same);
  - in `DisposeAsync`, call `detachCommandLinks` and dispose the reference.

### Security note

Command strings in `xch_cmd` come from server-rendered markup — help content shipped with
the server, or softcode that already requires the `PUEBLO_SEND`/`Send_OOB` power to emit
protocol tags (per `sharppueb.md`). The client simply sends back whatever the server placed
in `xch_cmd`; it introduces no new trust boundary beyond what typing the same command would.
Attribute values remain HTML-encoded.
Any future code path that turns *player-supplied* text into command-kind links (none exists today — help-topic links are doc-authored) would let one player craft a command another could click, so such a path must not be added without escaping/gating.

## Components & Boundaries

- **`SharpMUSH.MarkupString`** — owns the `LinkKind` type and all format rendering. Pure,
  unit-testable, no I/O.
- **`SharpMUSH.Documentation`** — owns markdown→markup mapping (which links are commands).
- **`SharpMUSH.Client`** — owns terminal click handling and command dispatch.

Each boundary is testable in isolation: markup renderers via TUnit string assertions; the
markdown mapping via TUnit on rendered markup; the client callback via its forwarding to
`ITerminalService`.

## Testing

- **MarkupString unit tests (TUnit):** each renderer (HTML/Pueblo/MXP/BBCode/ANSI) for
  `Command` vs `Url`; hint present vs absent; attribute encoding.
- **Serialization tests:** `AnsiStructure` with `LinkKind` round-trips; a legacy payload
  with no `LinkKind` field deserializes to `Url`.
- **Markdown renderer tests:** `[topic]` → command markup whose HTML is
  `xch_cmd="help topic"` with no `href`; `[text](http://x)` → `href` + `target="_blank"`;
  `[text](http://x "tip")` → hint present; `<http://x>` autolink → `href`.
- **Client:** a test that `RunCommandLinkAsync` forwards the command to
  `ITerminalService.SendAsync`. The JS event delegation itself is verified by running the
  app against the help command.

## Out of Scope

- Changing softcode functions (`tag`/`tagwrap`/`wshtml`/`html`) — they already let
  privileged players emit raw `xch_cmd`/`SEND`.
- Reworking MXP line-security mode prefixes (`ESC[0z`/`ESC[1z`) — tracked separately.
- Wiki `[[links]]` (already rendered as underlined non-links in the terminal).

## Files Touched (anticipated)

- `SharpMUSH.MarkupString/Markup/Markup.cs` — enum, `AnsiStructure`, `Create`, all `WrapAs*`/`ApplyDetails`.
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicInlineParser.cs` — set command marker.
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicLinkExtension.cs` (or a small shared type) — marker key constant.
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.Inlines.cs` — `RenderLink`/`RenderAutolink`.
- `SharpMUSH.Client/wwwroot/js/mush-monaco.js` — `attachCommandLinks`/`detachCommandLinks`.
- `SharpMUSH.Client/Components/GlobalTerminal.razor` — DotNetObjectReference, JSInvokable, lifecycle.
- Tests across `SharpMUSH.Tests` (markup + markdown + serialization) and client.
