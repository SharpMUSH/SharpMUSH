# Mobile Shell Foundation — Design Spec

**Date:** 2026-06-27 · **Branch:** `feature/terminal-windows` · **Base:** `main` (`1b47def`)

## Context

SharpMUSH.Client (Blazor WASM, MudBlazor) renders inside a hand-rolled CSS shell
(`.phosphor-shell`), **not** MudBlazor's `MudLayout`/`MudDrawer`. On a phone the
shell is unusable:

- `.phosphor-sidebar` is a **232px fixed inline flex column** (62px collapsed) — it
  eats ~60% of a 390px viewport and squashes page content into a ~140px strip.
- `.phosphor-topbar` packs a 220px search box + `⌘K` hint + language picker +
  terminal toggle + user menu, all `flex-shrink:0` → they overflow off-screen.
- `.phosphor-widget-aside` left/right zones are fixed-width inline columns.
- **There is not a single `@media` rule in `custom.css`.** The viewport meta tag is
  correct; the layout simply has no responsive behavior.

The `/play` page already received a targeted mobile patch (`minmax(0,1fr)`,
`min-width:0`, a `@@media (max-width:760px)` stack). This foundation generalizes
that work to the whole shell and establishes the conventions every page reuses.

## Goal

A responsive app shell plus a small, documented mobile CSS toolkit, so that:

1. The nav becomes an **off-canvas overlay drawer** below the breakpoint.
2. The topbar **condenses** to fit a phone without overflow.
3. Global widget asides get out of the way.
4. Page authors (the later page-batch sub-projects) have **shared primitives**
   (breakpoint, helper classes, touch-target sizing, `dvh` convention) to build on.

This is **Sub-project 0** of a comprehensive mobile campaign. It ships first because
every page renders inside the shell and depends on these primitives.

## Decisions (made during brainstorming; documented rather than gated)

The user delegated autonomous execution ("start with the shell, then do the pages in
parallel, come back when done"), so the following choices are recorded here as the
source of truth instead of being individually approved.

### Breakpoint
Single primary breakpoint: **`max-width: 760px` = "mobile"**. Chosen to match the
existing `/play` patch so the whole portal flips at one consistent width. Authored as
literal `760px` everywhere (CSS media queries can't read CSS custom properties in the
condition). A `--mobile-bp` token is added purely as documentation.

### Nav pattern — off-canvas overlay drawer
Below 760px the sidebar becomes `position: fixed`, full-height, `z-index` above
content, translated off-canvas (`translateX(-100%)`) by default and slid in when
open. A dimmed **backdrop** covers the content and closes the drawer on tap. The
drawer renders at its **full 232px width** (never the 62px rail) on mobile — icon
rails are a desktop affordance. Overlay (not push) is chosen because the content
column needs the full viewport width on a phone.

Open/close state: a new `_mobileNavOpen` bool in `MainLayout`. The existing hamburger
button drives the right thing per viewport via a tiny JS `matchMedia` check:
- **Mobile** (`≤760px`): toggles `_mobileNavOpen` (open/close the overlay).
- **Desktop** (`>760px`): toggles `_sidebarCollapsed` (existing rail collapse) — unchanged.

The drawer auto-closes on `LocationChanged` (navigating from a nav link) and on
backdrop tap. Desktop behavior is entirely unchanged.

### Topbar condensation
Below 760px:
- **Hide:** the inline search box (`.phosphor-search`), the `⌘K` kbd hint, the
  language picker, and the topbar divider. (A dedicated mobile search affordance is a
  follow-up handled in the content page-batch; search remains reachable via `/wiki`.)
- **Keep:** hamburger, page title (truncates with ellipsis), terminal toggle, user
  avatar menu.
- Reduce horizontal padding; the title gets `min-width:0` + ellipsis so it never
  pushes the controls off-screen.

### Widget asides
Below 760px the global left/right widget asides (`.phosphor-widget-aside`) are
`display:none`. They are supplementary, usually-empty zones; guaranteeing the content
column the full width matters more on a phone. Per-page widget placement on mobile is
deferred to the page batches if it proves necessary.

### Bottom terminal drawer
The command terminal drawer (`.phosphor-terminal`, fixed 300px) uses a taller,
viewport-aware height on mobile (`min(70dvh, …)`) so its input row isn't cropped by
the mobile address bar.

### Touch targets
Interactive shell elements (nav links, icon buttons, user button) get a **≥44px**
min touch dimension on mobile.

## The reusable mobile toolkit (the deliverable the batches consume)

Added to `custom.css`, documented inline, so every later page uses the same vocabulary:

- **Breakpoint:** always `760px`. In `.css` files use `@media (max-width: 760px)`; in
  Razor `<style>` blocks use `@@media (max-width: 760px)` (Razor escapes `@`). This
  escaping gotcha is called out in a comment because it already bit the `/play` patch.
- **Helper classes:**
  - `.mobile-only` — `display:none` above 760px (shown only on mobile).
  - `.desktop-only` — `display:none` at/below 760px (hidden on mobile).
  - `.mobile-stack` — forces `flex-direction:column` at/below 760px.
  - `.mobile-full` — forces `width:100%` at/below 760px.
- **Conventions (documented, not enforced):** use `100dvh`/`dvh` for full-height
  regions; min tap target 44px; `min-width:0` on any flex/grid child that holds wide
  content (terminal output, tables, code); prefer `minmax(0,1fr)` over bare `1fr`.

## Components touched

| File | Change |
|---|---|
| `wwwroot/css/custom.css` | All shell `@media (max-width:760px)` rules + helper classes + `--mobile-bp` token + toolkit doc comment |
| `Layout/MainLayout.razor` | `_mobileNavOpen` state, backdrop element, hamburger viewport-aware toggle, auto-close on nav |
| `Layout/NavMenu.razor` | (likely none — drawer is pure CSS on `.phosphor-sidebar`; verify no markup change needed) |
| `wwwroot/js/` (new small module or existing interop) | `isNarrow()` `matchMedia` helper for the hamburger |
| `wwwroot/index.html` | confirm `viewport-fit=cover` for notch-safe `dvh` (add if missing) |

## Data flow / behavior

```
hamburger click
  └─ JS isNarrow()?  ── yes ─► toggle _mobileNavOpen ─► .phosphor-shell gets
  │                                                      .nav-open ─► drawer slides in,
  │                                                      backdrop appears
  └────────────────── no ──► toggle _sidebarCollapsed (desktop rail; unchanged)

backdrop tap / LocationChanged ─► _mobileNavOpen = false ─► drawer slides out
```

No server calls, no SignalR, no auth changes. Pure client layout/CSS + a few lines of
component state and one JS helper.

## Testing

- **Live, visual:** run the portal (Server + ConnectionServer + Client over fixed
  Arango/NATS per the run recipe), drive with Playwright bundled chromium at a Pixel-8
  profile (390×844, touch). Capture and `claude-show` screenshots of: drawer closed,
  drawer open (backdrop), topbar condensed, and a content page at full width.
- **Diagnostics:** assert `document.documentElement.scrollWidth == innerWidth` (no
  horizontal page scroll) and zero elements overflowing the viewport right edge on
  representative pages.
- **Regression:** desktop (≥1280px) shell visually unchanged; `dotnet build` 0 errors
  (`TreatWarningsAsErrors`); existing bUnit layout tests still pass.

## Out of scope (handled by later sub-projects)

- Per-page content responsiveness (Play internals beyond the existing patch, wiki,
  admin tables, softcode editor, etc.) — Batches 1–4.
- A first-class mobile search experience.
- Mobile-specific widget layouts.

## Campaign decomposition (for reference; each its own spec later)

- **Batch 1 — Play & real-time:** `/play`, `/scenes*`, `/mail*`
- **Batch 2 — Content/read:** `/`, `/wiki*`, `/characters`, `/character/{name}`, `/help*`, `/apps/{slug}`
- **Batch 3 — Account/auth:** `/account`, `/settings*`, `/setup`, `/login`, `/register`
- **Batch 4 — Admin & tools:** `/admin/*`, `/softcode`
