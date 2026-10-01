# D1 Phase 1 — Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the desktop top bar and grouped sidebar with the D1 frame (README §3): a 72px icon rail, a shell-owned page-sidebar slot that pages fill and can collapse to a 62px strip, the Build & manage section, and a command palette, while mobile keeps its drawer and bottom tab bar.

**Architecture:** `NavRail` renders the desktop rail. `MainLayout` hosts a `SectionOutlet("page-sidebar")` in a shell column; `SectionShell` (from Phase 4) sends its sidebar there with `SectionContent` and owns the collapse button, remembered per section by `SidebarCollapseService` (localStorage). The Build and Manage gates move into one `BuildNavCatalog`, read by the rail, the new `BuildSidebar` and the mobile `NavMenu` drawer. `CommandPalette` opens from the rail's search button and ⌘K/Ctrl+K.

**Tech Stack:** .NET 11, Blazor WASM (`SectionOutlet`/`SectionContent`), MudBlazor 9, TUnit + bUnit.

**Spec:** `docs/design/d1/README.md` §3 (frame, navigation mapping), §9 (acceptance), §10 Q1–Q3; board `20-patterns.png`; the collapsed strips on boards 22, 24, 26.

## Rulings on the open questions (the user said "keep going until done"; each is stated in the PR)

- **Q1** Follow the README's proposal: a search icon at the top of the rail opens a command palette (new, minimal); the terminal toggle sits at the bottom of the rail; no page title on desktop; the global TopBar zone renders as a slim strip above main only when it has placements. The language picker moves to the rail's bottom cluster rather than the account menu, because an anonymous visitor has no account menu and must still be able to change language.
- **Q2** Help gets a rail item after Characters: it is reference reading for everyone, and hiding it under Wiki's Browse would make it undiscoverable from every other section.
- **Q3** Collapse is remembered per section in localStorage, as proposed.

## Global Constraints

- Every policy gate and route stays exactly as today, including fallback pairs (`queue.inspect` → `queue.inspect.own`, `jobs.manage.own` → `jobs.manage`) and the snapshots claim check (`perm` `snapshots.capture`/`restore`).
- The global scope's zones (TopBar, LeftSidebar, RightSidebar, Footer) keep working.
- Mobile (≤760px or coarse pointer): the drawer and bottom tab bar stay; the page sidebar becomes an off-canvas panel opened from the mobile header.
- ui-patterns §14; only `shell.css` uses `@media` and `position: fixed`.
- Player-facing strings in all 16 resx.

## Review Focus

1. A viewer with only `queue.inspect.own` sees Diagnostics in Build & manage and the rail's shield; a viewer with nothing staff-side sees no shield (Task 1, Task 3).
2. Collapse on the wiki does not collapse config; a reload keeps each (Task 2).
3. Every existing page still renders its body when no page sidebar exists, and the pagebar column disappears (Task 5).
4. ⌘K does not fire while typing in the terminal input or a text field that handles its own K (Task 6).
5. At 390px the rail is hidden, the drawer holds the full menu, and the page sidebar opens as a panel (Task 5).

---

### Task 1: One catalogue of Build & manage gates
**Files:** `Layout/BuildNavCatalog.cs` (entries: href, icon, label key, `AnyOfPolicies`, optional claim check, match), `Layout/NavMenu.razor` Build and Manage groups render from it through an `AuthorizedNavItems` component. Tests: `BuildNavCatalogTests` (each entry's gates), existing `NavGroupVisibilityTests`/`NavMenuTests` stay green.

### Task 2: Page-sidebar slot and collapse memory
**Files:** `Services/SidebarCollapseService.cs` (per-key bool in localStorage `sharpmush.sidebar.<key>`), `Components/Kit/SectionShell.razor` (+css; `Key`, `Nav` as `RenderFragment<bool>`, `SectionContent SectionName="page-sidebar"`, collapse button 36×36 radius 10 `aria-expanded`, `PageSidebarRegistry` registration), `Services/PageSidebarRegistry.cs` (count of mounted section shells, change event), layouts pass `Key` and `Context="collapsed"`. Tests: `SectionShellTests` (renders into an outlet, toggle, remembered per key, registry).

### Task 3: The rail
**Files:** `Layout/NavRail.razor` (+css in shell.css): logo; Home, Play (session dot), Scenes, Wiki, Characters, Help, Mail (only while a character is active); novel app sections by the icon of their lowest-Order app; spacer; Build & manage shield when any catalogue entry is visible; search (palette), terminal toggle, language, account avatar with `AccountPanel`. 44×44 targets, radius 14, `aria-current="page"` on the active section, `aria-label` on every icon. Tests: `NavRailTests`.

### Task 4: Build & manage section
**Files:** `Components/Admin/BuildSidebar.razor` (+css), `Layout/BuildLayout.razor`, `Pages/Admin/_Imports.razor` (`@layout BuildLayout`), `@layout BuildLayout` on `SoftcodeEditor` and the other Build/Manage pages outside `Pages/Admin`, `ConfigSidebar` "‹ Build & manage" row, apps under "Apps". Tests: `BuildSidebarTests`.

### Task 5: MainLayout and shell.css
**Files:** `Layout/MainLayout.razor`, `wwwroot/css/shell.css`: rail column; pagebar column (`SectionOutlet`) only while a section shell is mounted, 232/62px; desktop top bar removed; TopBar zone strip; mobile header keeps hamburger, a section-sidebar button and the terminal; mobile pagebar off-canvas. Tests: `MainLayoutShellTests`, updated shell tests (`ContentContainerTests`, `DesignTokensTests`).

### Task 6: Command palette
**Files:** `Components/CommandPalette.razor` (+css), `wwwroot/js/layout.js` (⌘K/Ctrl+K hook that ignores editable targets), items: visible destinations (rail + catalogue), "Search the wiki for …", "Find a character named …". Arrow keys, Enter, Escape; focus returns to the opener. Tests: `CommandPaletteTests`.

### Task 7: Visual check, gates, review, PR
- [ ] Screenshots at 1280×800 and 390 for /, /wiki, a wiki page, /character/<name>, /admin/config, /play; compare with boards 20–26; `claude-show`.
- [ ] Format gate ×2; full bUnit; `SharpMUSH.Tests` client classes; node suite; `validate_resx.py`; responsive sweep.
- [ ] Fresh reviewer; fix pass; push `worktree-d1-phase-1-shell`; PR stacked on Phase 4.
