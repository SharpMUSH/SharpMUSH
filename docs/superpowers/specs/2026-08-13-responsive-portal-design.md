# Responsive Portal — Design Spec

**Date:** 2026-08-13 · **Branch:** `feature/responsive-portal` · **Base:** `main` (`11834d13`)

Successor to `2026-06-27-mobile-shell-foundation-design.md`, which shipped the mobile
shell and batches 1–3 and explicitly deferred Batch 4 (admin & tools). This spec finishes
that campaign *and* replaces its responsive model, because the model it established
cannot express the cases the portal actually has.

## Context

`SharpMUSH.Client` is 108 Razor views (65 routable pages) with 66 scoped stylesheets and
one 1565-line global `custom.css`. Measured state at `11834d13`:

- **One content breakpoint.** `@media (max-width: 760px)`, plus a `(pointer: coarse)`
  chrome tier. Nothing between a phone and a 3840px monitor is described.
- **21 scoped stylesheets contain no `@media` at all** — almost all of `/admin/*`
  (Dashboard, Players, PlayerDetail, Moderation, AdminCharacters, AdminProfiles,
  AdminMedia, AdminServer, AdminWiki, AdminWikiAssets, BannedNames, ConfigIndex,
  ImportConfig, ImportDatabase, SuggestionManagement), plus `ObjectBrowser`,
  `HelpEntryPanel`, `ServerStartupGate`, `OnboardingLayout`, and two widgets.
  `AdminAccounts` and three dialogs have no stylesheet at all.
- **No `max-width` on `.phosphor-main`.** Content is full-bleed at any width.
- **Zero `@container` / `container-type`.** No component sizes to its own box.
- **117 `!important` declarations** in source CSS (84 of them in the scoped bundle),
  which are pages manually simulating a cascade order that CSS can now express directly.

### The fact that decides the architecture

The content column's width is **not a function of the viewport**:

```
.phosphor-shell  =  sidebar (232px | 62px collapsed | 0 when drawered)
                 +  left widget aside   (admin-configured width)
                 +  content column
                 +  right widget aside  (admin-configured width)
```

Both aside widths come from runtime settings — `MainLayout.razor:34` and `:128` write
`style="width:@(_layout.Settings.LeftSidebarWidth)"`. So at a 1280px viewport a page may
receive ~750px, or ~1210px, depending on state no stylesheet can see. Every viewport
media query in a `.razor.css` today is therefore guessing, and is wrong whenever the
sidebar is expanded or an aside is configured.

This is not a tuning problem. A page cannot ask the viewport how wide *it* is.

## Goal

Two outcomes, in this order:

1. **A CSS architecture with an enforceable ownership boundary** between styles that
   describe the *device* and styles that describe a *page or component*.
2. **Every one of the 108 views correct** at phone, portrait-tablet, thin desktop window,
   and fullscreen widths — including with the sidebar expanded and asides configured.

## Decisions

Approved during brainstorming; recorded here as the source of truth.

| Decision | Choice |
|---|---|
| Ultrawide (>1600px) | Cap and centre content; full-bleed pages opt out |
| Tablet / portrait tier | Yes — a real tier, expressed as a container tier |
| Scope | All 108 views: finish Batch 4 *and* re-audit batches 1–3 |
| Verification | Playwright screenshot + overflow sweep, plus a guard test in CI |
| Cascade | Full `@layer` adoption, MudBlazor imported into a layer |
| Migration | Migrate **all** scoped CSS from `@media` to `@container` |

## Architecture

### Three layers of ownership

| Layer | Lives in | Owns | Query type |
|---|---|---|---|
| **Shell / viewport** | `css/shell.css` | sidebar ↔ off-canvas drawer, bottom nav, topbar condensation, touch ergonomics, safe-area insets, terminal drawer height | **`@media` only** |
| **Content seam** | `css/shell.css`, `.phosphor-page` | declares the query container; applies the ultrawide cap | both, in one place |
| **Page / component** | `*.razor.css` | everything inside the content column | **`@container` only** |

The boundary reduces to one testable sentence:

> **`@media` never appears in a `.razor.css`. `@container` never appears in the shell.**

The shell decides *how much width content gets*. Content asks its container *how much it
got*. Neither needs to know about the other, and the runtime-configurable asides stop
being a hidden variable.

### Container vocabulary

`.phosphor-page` — the wrapper `MainLayout` puts around `@Body`, inside the scrolling
`.phosphor-main` — declares:

```css
.phosphor-page {
	container: page / inline-size;
}
```

Page stylesheets query the named container. Tiers are `rem`, so they scale with the
user's root font size (the repo's existing rule: type and spacing in `rem`; borders,
shadows, fixed dimensions and radii in `px`):

| Tier | Condition | Intent |
|---|---|---|
| Narrow | `@container page (max-width: 48rem)` | single column, condensed — replaces today's 760px phone rules |
| Medium | `@container page (max-width: 64rem)` | 3+ column grids → 2; fixed sidecars (230–340px) stack; portrait tablet and thin desktop windows land here |
| Roomy | `@container page (min-width: 90rem)` | optional richer layouts; most pages need nothing |

Roomy gates on `min-width`, not `max-width` — it is an upgrade applied as the container grows,
the reverse of narrow/medium. A `max-width` tier at 90rem or wider is not just off-spec: the
shell caps `.phosphor-page` at `--content-max` (1400px) once the viewport passes 1601px (see
below), so a `max-width` threshold above 1400px is either unreachable at ordinary viewports
(the container simply never gets that wide with a sidebar subtracted) or, worse, flickers —
with the sidebar collapsed, widening the window from 1600px to 1601px snaps the container from
just under the cap straight down to it, re-crossing a >1400px threshold in the same pixel. This
cost a round of review on Task 9 before the guard test learned to check tier direction, not just
the literal.

Tiers nest by width, not by device. A page in a narrow content column on a 27" monitor
gets the narrow layout, which is the correct answer and one the old model got wrong.

Both `max-width` tiers match below 48rem, so **narrow blocks are authored after medium
blocks** in a stylesheet and win on order. Stylesheets state the tiers in the fixed order
roomy → medium → narrow so the cascade is uniform and reviewable.

**Self-sizing components** — zone widgets, cards in an `auto-fit` grid, `ObjectBrowser`,
`SchemaFormRenderer` — declare their **own** container and query it unnamed:

```css
.widget-root { container-type: inline-size; }
@container (max-width: 30rem) { /* … */ }
```

A widget then renders correctly both full-width on `/` and in a 300px aside, which is
impossible under viewport queries.

**Containment safety.** `container-type: inline-size` makes the element a containing block
for `position: fixed` descendants. Audited: no `position: fixed` exists in any scoped
stylesheet or Razor inline style; the only two `position: sticky` uses
(`DynamicConfig.razor.css`, `WikiDisplay.razor.css`) are unaffected by containment.
MudBlazor renders dialogs and popovers through a body-level portal provider, so they are
outside every container. The change is safe as of this audit; the guard test keeps it
that way by failing on `position: fixed` inside scoped CSS.

### Ultrawide cap

```css
@media (min-width: 1601px) {
	.phosphor-page { max-width: var(--content-max); margin-inline: auto; }
}
.phosphor-page:has(> .full-bleed) { max-width: none; }
.phosphor-page:has(> .full-height) { height: 100%; }
```

`--content-max: 1400px`. A page opts out by putting a class on its own root element — no
cascading parameter, no layout service, no ancestor-class problem.

**The two opt-outs are independent classes, deliberately.** `full-bleed` answers "this page
wants the whole window width"; `full-height` answers "this page needs a definite height to
size its own panes". Bundling them into one class hid a defect: `/mail` needs the height for
its three-pane scrolling but should keep the cap, since a mail client stretched across
2560px is worse than a capped one. A page needing both carries both.

Opting out of the cap: `/play`, `/softcode`, scene live, wiki edit split, the layout editor,
and the wiki diff view — working surfaces rather than reading surfaces. Taking the height
without the cap: `/mail`, and `/play`'s loading and gate branches, whose content is a
centred card that gains nothing from the extra width.

The cap is the one place a viewport query and a container declaration meet, and it lives
in the shell where the boundary rule permits it.

### Cascade layers

```css
@layer mud, tokens, shell, utilities;
@import url("../_content/MudBlazor/MudBlazor.min.css") layer(mud);
```

Unlayered styles beat every layered style regardless of specificity. The scoped bundle
(`SharpMUSH.Client.styles.css`) stays unlayered and therefore wins over both MudBlazor
and our globals automatically — which is exactly what the 117 `!important` declarations
are hand-simulating. They are deleted as part of the sweep, not annotated.

`index.html` drops the MudBlazor `<link>` and gains
`<link rel="preload" as="style" href="_content/MudBlazor/MudBlazor.min.css">` so the
import is not serialized behind `custom.css`.

### File split

`custom.css` today mixes design tokens, the app shell, MUSH syntax colours, a popover
width workaround, and the mobile toolkit in one 1565-line file. It becomes a manifest:

| File | Contents |
|---|---|
| `css/tokens.css` | design tokens, font faces, per-`:lang` mono stack |
| `css/shell.css` | shell, sidebar, topbar, terminal drawer, bottom nav, `.phosphor-page`, **all viewport media queries** |
| `css/utilities.css` | `.scroll-x`, `.toolbar-row`, `.mobile-only`, `.desktop-only`, tap-target helpers |
| `css/mush-syntax.css` | `.mush-*` token colours (content styling, not layout) |
| `css/globals.css` | documented escape hatches — styles that *must* be global because scoped CSS cannot reach them (e.g. `.char-picker`, which sits on a `MudPaper` carrying MudBlazor's scope) |
| `css/custom.css` | `@layer` declaration + `@import`s only |

`globals.css` exists so the escape hatches are a named, reviewable list rather than
accumulating silently in the middle of the shell.

### Modern CSS, where it removes code

Adopted because each one deletes rules or JavaScript, not for novelty:

- **`clamp()`** for fluid type and gutters — removes breakpoints instead of adding them.
- **`subgrid`** on card grids so card internals align across a row without fixed heights.
- **`field-sizing: content`** on textareas (`/mail/compose`, wiki edit) — auto-growing
  inputs with no JS measurement.
- **Logical properties** (`margin-inline`, `padding-block`, `inset-inline`) throughout;
  `margin-inline: auto` is already the cap's centring mechanism.
- **`overscroll-behavior: contain`** on the nav drawer, terminal drawer, and scroll panes —
  stops scroll chaining on touch.
- **`content-visibility: auto`** + `contain-intrinsic-size` on long lists (wiki index,
  players, help topics).
- **`scroll-margin-block-start`** for in-page anchors landing under the sticky topbar.

Deliberately **not** adopted: `light-dark()` (theming is DB-driven CSS variables), view
transitions (SPA routing needs JS orchestration; out of scope), `@property`, `@container
style()`.

**Spike before use:** native CSS nesting — verified unsafe for descendant rules, verified
safe for nested at-rules and same-element compounding; permission is narrowed accordingly.

For a flat rule the rewriter attributes the *innermost, matched* selector:
`ObjectBrowser.razor.css:37`'s `.ob-item--on .ob-item-name { }` builds to
`.ob-item--on .ob-item-name[b-3gnim3jhv4] { }`, so the rule can only ever match an element
the component itself rendered. A scoped `EmptyState.razor.css` with `.nesting-spike {
& .child { } @container (max-width: 48rem) { } }` built and emitted
`.nesting-spike[b-pi9wsximhx] { & .child { } @container (...) { } }` — the rewriter
attributes only the *outer* selector and leaves `& .child` untouched. The browser resolves
that to `.nesting-spike[b-xxxxx] .child`: a plain descendant selector whose scope
requirement sits on the ancestor only — the opposite of the flat form, and a weaker
guarantee, not an equivalent one.

Whether that gap is reachable was checked directly rather than assumed. `CharacterDirectoryWidget.razor`,
given a temporary scoped stylesheet (scope `b-5qz6nm2reg`), renders `<EmptyState Class="child">`
on its failed-roster path; `EmptyState` is a separate component with no scoped css of its
own. Rendered through bUnit, the real DOM is
`<div class="nesting-spike-parent" b-5qz6nm2reg>` … `<div class="mud-alert mud-alert-outlined-info
mud-elevation-0 child" …>` — `EmptyState`'s element is a literal HTML descendant of the
parent's scoped div and carries the `child` class, but never the parent's `b-5qz6nm2reg`
attribute. A rule `.nesting-spike-parent[b-5qz6nm2reg] .child { }` (what `& .child { }`
desugars to) matches that element purely by DOM structure — it leaks onto whatever a nested
child component renders with a colliding class name, which is the norm here (widgets inside
zones, layouts wrapping pages), not an edge case.

Nesting is permitted in scoped (`*.razor.css`) stylesheets only for forms that stay on the
*same element* as the outer selector: nested at-rules (`@container`, `@media`, `@supports`)
and `&`-compounding (`&:hover`, `&.modifier`, `&[attr]`). A nested rule that introduces a
descendant combinator (`& .child`, `& > .child`, `& x`) is **not** permitted in scoped files
— write it flat (`.outer .child { }`) so the rewriter puts the scope attribute on the
matched element itself, the same guarantee `ObjectBrowser.razor.css` already relies on.
Global files remain unrestricted either way.

## Work breakdown

**Phase 0 — Foundation.** Nesting spike; file split; `@layer` adoption and MudBlazor
import; `.phosphor-page` container and cap in `MainLayout` + `shell.css`; tier tokens;
utilities. Shell media queries stay as they are (they are correct — they describe the
device) apart from moving file.

**Phase 1 — Guard test.** Land the test before the sweep, so each batch is verified as it
lands rather than audited afterwards.

**Phase 2 — Sweep.** One commit per batch, each diff small enough to read:

- **A — Admin core** (no `@media` today): Dashboard, Players, PlayerDetail, Moderation,
  AdminCharacters, AdminProfiles, AdminMedia, AdminServer, AdminWiki, AdminWikiAssets,
  BannedNames, ConfigIndex, ImportConfig, ImportDatabase, SuggestionManagement,
  AdminAccounts.
- **B — Admin partial + dialogs**: AdminConfig, DynamicConfig, AdminLayouts, LayoutEditor,
  the five Packages pages, Restrictions, Sitelock, Roles, Applications, and the three
  dialogs (`RoleEditDialog`, `ApplicationEditDialog`, `WidgetConfigDialog`).
- **C — Shared components**: ObjectBrowser, HelpEntryPanel, ServerStartupGate,
  OnboardingLayout, CharacterDirectoryWidget, WelcomeTextWidget, WikiAssetPicker,
  GlobalTerminal, HelpDrawer, ConfigNavDrawer, the Schema renderers, and the widgets that
  currently ship no stylesheet.
- **D — Migrate shipped pages**: the ~45 stylesheets that already carry `@media` convert
  to `@container` and gain the medium tier: Home, Play, Scenes\*, Mail\*, Wiki\*,
  Characters, CharacterProfile, Help\*, Account, Settings\*, Setup, Login, Register,
  CharacterCreate, SoftcodeEditor, DynamicApplication.

Fixed `repeat(N, 1fr)` grids become `repeat(auto-fit, minmax(Npx, 1fr))` wherever the
track count is arbitrary; self-tiering grids need no tier rule at all and are preferred
over adding one.

## Verification

### Guard test — `SharpMUSH.Tests.BUnit`

A convention test over the source tree, so regressions cannot land silently:

1. No `@media` in any `*.razor.css`. *(the ownership rule)*
2. No `@container` in `css/shell.css`. *(the ownership rule, other direction)*
3. No `position: fixed` in any `*.razor.css`. *(containment safety)*
4. Every routable page has a scoped stylesheet, or appears on an explicit exemption list
   with a stated reason.
5. Every page stylesheet declares at least one container tier, or is exempt.
6. Container tier conditions use only the three sanctioned literals (`48rem`, `64rem`,
   `90rem`) — prevents tier drift.
7. Viewport breakpoint literals in `shell.css` match `sharpmushLayout` in `layout.js`.
8. No `!important` in scoped CSS. *(guards the layer work — the reason they existed is gone)*

### Playwright sweep

Against the dev stack, using the bundled Playwright chromium:

```bash
docker compose up -d                                    # ArangoDB + NATS
dotnet run --project SharpMUSH.Server                   # https://localhost:8081
dotnet run --project SharpMUSH.ConnectionServer         # :4201 telnet, :4202 http
dotnet run --project SharpMUSH.Client                   # https://localhost:7102
```

A dev build of the Server does **not** serve the WASM client, so the sweep drives the
Client's own dev host on 7102.

Per route in the route map, at 390×844, 820×1180, 1280×800, 2560×1440:

- screenshot for review;
- assert `document.scrollingElement.scrollWidth <= window.innerWidth` — horizontal
  overflow becomes an objective pass/fail rather than an eyeball call.

Plus two extra passes at 1280px that the old model could not express:

- sidebar **expanded** (content ≈1050px);
- a wide right aside configured (content ≈750px).

These are the cases container queries exist to fix, so they are the cases that prove the
architecture rather than just the styling.

## Risks

- **`@layer` regressions.** Removing 117 `!important` will expose places where one was
  masking an unrelated specificity bug. The screenshot sweep is the detector; layer work
  lands in Phase 0 with its own sweep before the page batches start.
- **MudBlazor load order.** Moving Mud from `<link>` to `@import layer(mud)` serializes it
  behind `custom.css`. Mitigated by the preload hint; verify first-paint timing in the
  sweep and revert to a layered `<link>` alternative if it regresses.
- **Scale.** 108 views. Mitigated by one commit per batch and by preferring `auto-fit`
  grids over hand-written tier rules.
- **Nesting spike may fail.** If the scoped-CSS rewriter mishandles nesting, nesting stays
  confined to global files. This changes nothing else in the design.

## Out of scope

- MudBlazor component-level theming beyond deleting `!important` overrides.
- A first-class mobile search experience (still deferred from the 2026-06-27 spec).
- RTL locale support. Logical properties are adopted because they cost nothing extra, not
  because RTL is being enabled — no RTL locale ships today.
- View transitions on route change.
