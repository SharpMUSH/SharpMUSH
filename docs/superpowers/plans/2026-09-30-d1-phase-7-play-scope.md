# D1 Phase 7 — Play scope, channels and pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play becomes a layout scope (README §7.4, board 13) whose aside is admin-composed from Here and Exits widgets and game applications, channels and pages read from the comm feed (§7.3, the comm-data branch), and choosing a channel or a conversation opens the channel view in main (board 06).

**Architecture:** `LayoutScopes.Play` (zone RightSidebar) with default `[Here, Exits]`; `HereWidget`/`ExitsWidget` wrap Phase 6's cards and read the play terminal's `OobChannelStore`. Play's aside renders through `ZoneRenderer` for that scope. `RegisteredApplication` gains optional `Scope` and `OobPackage`; an application widget with an `OobPackage` renders that package's payload from the store as `SchemaData`. `OobCommFeed` (comm-data branch, merged here) replaces `EmptyCommFeed`; `CommView` is the board-06 channel view, with its own composer that sends `@chat` or `page`.

**Tech Stack:** .NET 11, Blazor WASM, MudBlazor 9, TUnit + bUnit; softcode package manifest (`PackageManifestService`).

**Spec:** `docs/design/d1/README.md` §5.1, §7.3, §7.4; boards 06, 13; `docs/softcode/comm-feed-handler.md` (comm-data branch).

**Stacks on:** Phase 6 (worktree-d1-phase-6-play-ui) merged with comm-data (worktree-d1-comm-data, after its review fixes).

## Global Constraints

- Channel and page text renders as text, never as HTML.
- A widget placed in the Play scope that needs the play connection reads the play terminal's store, never the command terminal's.
- `ICommFeed` stays the only thing Play knows about channels; nothing casts to `OobCommFeed`.
- Keep every existing layout loadable: a saved layout of another scope is untouched; a Play scope with no saved layout gets the default.
- ui-patterns §14; player-facing strings in all 16 resx.

## Review Focus

1. A channel line or page containing `<script>`, markup or `[ansi(...)]` shows literally (Task 2).
2. Lines for the key being viewed do not raise its unread count; leaving the view restores counting (Task 2).
3. A page conversation with a name containing spaces reaches the right people (send by dbref from the objids) (Task 2).
4. An application whose `oob_package` payload is malformed or absent renders its empty state, not an exception (Task 4).
5. A layout saved before the Play scope existed still loads for every other scope, and the layout editor lists Play (Task 3).

---

### Task 1: Merge the comm feed
Merge `worktree-d1-comm-data`; resolve `ICommFeed.cs` (take comm-data's, which adds `Viewing`), drop `EmptyCommFeed` and its registration (tests get a `FakeCommFeed` in Tests.BUnit), keep one `ICommFeed` registration (`OobCommFeed`). Full BUnit green.

### Task 2: Channel view (board 06)
`Components/Play/CommView.razor`: header (`#` + channel name, or the conversation's names; sub-line), lines grouped by day ("Today", dates) and by consecutive author, a "New" divider before the first line unread when the view opened, author initials/portrait from the directory by objid, plain-text bodies, and a composer ("Message #Public" / "Message Wren Halloway") sending `@chat <channel>=<encoded>` or `page <dbrefs>=<encoded>`. Opening sets `Viewing` and `MarkRead`; closing clears `Viewing`. Play shows `CommView` in main while a key is current (the sidebar marks it); the In scene row returns to the scene. Tests `CommViewTests`, Play page tests.

### Task 3: Play as a layout scope
`LayoutScopes.Play` ("play", RightSidebar), `LayoutService` default `[Here 0, Exits 1]`, `HereWidget`/`ExitsWidget` + descriptors registered in `BuiltInWidgets` (AllowedZones RightSidebar, ConfigType none), resx `LayScopePlay*`/`Wid*`. Play's aside renders `ZoneRenderer` for the scope; the sheet still opens from Here. Tests: scope listed, default layout, widgets render the store's room, Play aside comes from the layout.

### Task 4: Registry `scope` and `oob_package`; Apps in the Play sidebar
`RegisteredApplication` + client `PortalApplication` gain `Scope` and `OobPackage` (optional, trailing); `PackageManifestService`/`Writer` read and write `scope` and `oob_package`; `ApplicationsController` DTO carries them. `SchemaWidget` for an application with `OobPackage` reads that package from the play store (subscribing to `ChannelUpdated`) and renders it as `SchemaData`; malformed JSON renders the empty state. The Play sidebar lists page apps with `NavPlacement == "Play"` under "Apps". Tests: manifest round-trip, widget renders pushed payload and updates, malformed payload, sidebar Apps.

### Task 5: Visual check, gates, review, PR
