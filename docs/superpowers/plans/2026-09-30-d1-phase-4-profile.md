# D1 Phase 4 — Character profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/character/{Name}` and the Characters section match boards 25–26: a Characters page sidebar, the 236px profile banner with portrait and name colour, Details and Biography cards, and an aside with Gallery, Recent scenes and Often plays with.

**Architecture:** Server work is the gallery bridge (spec §2: `IsBanner`, one shared `GalleryEntry` record, mirroring into `IMAGE`, `IMAGE`BANNER`, `IMAGE`ALT`) and a participant filter plus a partners summary on the scene API (§7.5). The client gets a `CharactersLayout` with `CharactersSidebar`, a rewritten `CharacterProfile` page built on `GlassBanner` (new `Lead`, `TitleColor`, `BottomActions`, `Hue` parameters), schema sections rendered as kit cards, and three aside widgets in the profile scope's `RightSidebar`.

**Tech Stack:** .NET 11, Blazor WASM, MudBlazor 9, TUnit + bUnit.

**Spec:** `docs/superpowers/specs/2026-09-29-image-attributes-and-oob-v2-design.md` §2–§3 and `docs/design/d1/README.md` §6.3, §7.5 (boards 25, 26).

## Global Constraints

- README §1 decisions are fixed; trust code for behaviour, spec for looks.
- Every authorization policy and route stays. Gallery writes stay owner/staff only.
- Player-facing resx keys (`Nav*`, `Wid*`) go into all 16 resx files (`tools/i18n/add_keys.py`).
- ui-patterns §14: `@container page` tiers only, no `!important`, no `position: fixed` in scoped CSS, `::deep` anchored.
- Image URLs from data render only through `ImageUrlPolicy.IsRenderable`.
- Profile-handler 1.5 fields (`image`, `banner`, `color`) arrive with PR #1440; this phase renders them when present and never depends on them.

## Review Focus

1. A character with no gallery, no banner and no colour renders the hue gradient, initials portrait and default text colour, with no broken images (Task 5).
2. A `color` field that is not `#rrggbb` is ignored, never injected into `style` (Task 5).
3. Setting a banner, then deleting it, clears `IMAGE`BANNER`; deleting the icon promotes the next and re-mirrors `IMAGE` (Task 1).
4. A private scene the viewer cannot see never appears in Recent scenes or feeds Often plays with (Task 2).
5. An unreadable directory or scene API shows the aside card's unavailable state, not an empty "no scenes" claim (Task 6).

---

### Task 1: Gallery bridge (server)

**Files:** Create `SharpMUSH.Contracts/API/GalleryEntry.cs`, `SharpMUSH.Server/Services/GalleryRules.cs`; modify `SharpMUSH.Server/Controllers/GalleryController.cs`; tests `SharpMUSH.Tests/Gallery/GalleryRulesTests.cs`, `SharpMUSH.Tests.Integration/Gallery/GalleryMirrorTests.cs`.

- `public record GalleryEntry(string AssetId, string FileName, string Url, string? Caption, int Order, bool IsIcon, bool IsBanner = false)` in `SharpMUSH.Library.API` namespace alongside the wiki DTOs.
- `GalleryRules.Normalize(IEnumerable<GalleryEntry>)` → ordered, re-numbered, at most one icon (first wins; none → first entry becomes icon when any exist) and at most one banner (first wins; none stays none).
- `GalleryRules.Mirror(IReadOnlyList<GalleryEntry>)` → `(string Image, string Banner, string Alt)`: icon URL, banner URL, icon caption; empty strings when absent.
- Controller: every write (upload, replace, delete) runs `Normalize`, writes `PROFILE`GALLERY`, then sets `IMAGE`, `IMAGE`BANNER`, `IMAGE`ALT` to the mirror (an empty value clears).
- [ ] Unit tests for Normalize and Mirror; integration test: upload two images, PUT with the second as banner, read the three attributes through `IAttributeService`; delete the banner, `IMAGE`BANNER` is empty.
- [ ] Commit `feat(gallery): IsBanner, one shared entry record, mirror into the IMAGE attributes`.

### Task 2: Scene participant filter and partners (server)

**Files:** `SharpMUSH.Plugins.Scene/Web/SceneController.cs`; tests in `SharpMUSH.Tests.ScenePlugin/SceneControllerParticipantTests.cs`.

- `GET /api/scenes?participant=#42[&count=]` → the participant's scenes (storage `mine` filter with the participant as the member), newest first, each gated by the caller's `CanSeeAsync`.
- `GET /api/scenes/partners?participant=#42[&count=6]` → `[{ dbref, name, scenes }]`: members of the participant's visible scenes (last 50), excluding the participant, counted, most shared first, ties by name.
- [ ] Tests: private scene hidden from a stranger in both endpoints; partners counts and orders; bad dbref → 400.
- [ ] Commit `feat(scenes): participant filter and scene partners for the profile`.

### Task 3: Client data

**Files:** `Services/GalleryService.cs` (uses `GalleryEntry`; `SetBannerAsync`, `SetIconAsync` helpers over Replace), `Services/CharacterDirectoryService.cs` (`CharacterSummary.Image` optional), new `Services/CharacterProfileService.cs` (`GetAsync(name)` → `ServerResult<CharacterProfile>` with `Name, Objid, Dbref, Image, Banner, Color, Role`), `Services/SceneService.cs` (`GetParticipantScenesAsync(dbref, count)`, `GetPartnersAsync(dbref, count)`), `Models/SceneModels.cs` (`ScenePartner`). Register `CharacterProfileService` in `Program.cs`.

- `CharacterProfile.Color` is kept only when it matches `^#[0-9a-fA-F]{6}$`.
- [ ] Tests in `SharpMUSH.Tests.BUnit/Services/CharacterProfileServiceTests.cs` and `SceneServiceParticipantTests.cs` (fake handler).
- [ ] Commit `feat(client): profile fields, participant scenes and partners`.

### Task 4: Characters section sidebar and layout

**Files:** `Components/Characters/CharactersSidebar.razor` + css, `Layout/CharactersLayout.razor` + css, `Pages/Characters.razor` (+css) and `Pages/CharacterProfile.razor` use `@layout CharactersLayout`; `Pages/CharacterCreate.razor` too.

- Header "Characters" + "● N online · N characters"; search form → `/characters?q=`; **Online now** (up to 8 avatar rows, current character highlighted, "All N online" link → `/characters?online=1`); **Your characters** (from `AccountAuthService.GetCharactersAsync`, sub "you" on the acting one) plus "Create a character" (`/characters/new`); **Browse**: All characters (count) and Newest (created in the last 14 days, count; the board's "Recently approved" — no approval data exists).
- `/characters` restyled: `PlainPageHeader`, category `TypeChips`, a grid of `PortraitTile`s; `?q=` and `?online=1` honoured.
- [ ] Tests `CharactersSidebarTests`, `CharactersPageD1Tests`.
- [ ] Commit `feat(characters): section sidebar and layout; directory as portrait tiles`.

### Task 5: Profile banner, Details and Biography

**Files:** `Components/Kit/GlassBanner.razor` (+css; `Lead`, `TitleColor`, `BottomActions`, `Hue`), `Pages/CharacterProfile.razor` (+css), `Components/Schema/SchemaViewRenderer.razor` (+css: sections as `KitCard`s, field elements in an auto-fill grid), `Components/Widgets/WikiBodyWidget.razor` (+css: `KitCard` "Biography" with "Wiki page · edited {ago}" and History), `Components/WikiView.razor` (`OnArticle` callback).

- Banner: Height 236, `TitleSize="character"`, `HeadingLevel=1`, image = profile `Banner` → gallery banner → none (hue gradient from `NameHue`); `Lead` = 104px portrait (radius 18; icon image or initials); `TitleColor` = profile colour; `Secondary` = role line + pills (in a scene → `/play` link pill, Online/Idle when known, dbref); `BottomActions` = Mail (glass, `/mail/compose?to=`) and Page (primary, `/play?page=`); `Actions` = Full image (opens the viewer) when an image exists.
- [ ] Tests `ProfileBannerTests` (image fallback chain, colour validation, pills, actions, heading), `SchemaViewRendererKitTests`, `WikiBodyBiographyTests`, GlassBanner additions in `GlassBannerTests`.
- [ ] Commit `feat(profile): D1 banner, Details and Biography cards`.

### Task 6: Aside widgets and defaults

**Files:** `Components/Widgets/CharacterGalleryWidget.razor` (+css; KitCard "Gallery · N", View all, icon large with star, two small tiles, `Components/Kit/ImageViewer.razor` overlay with prev/next/captions and owner actions: upload, set icon, set banner, delete), new `RecentScenesWidget`, `OftenPlaysWithWidget` (+ descriptors in `Widgets/`, registration in `Program.cs`), `LayoutService` profile default RightSidebar = CharacterGallery, RecentScenes, OftenPlaysWith; `WikiDisplay` Mentioned tiles use directory images.
- [ ] Tests `GalleryAsideTests`, `ImageViewerTests`, `RecentScenesWidgetTests`, `OftenPlaysWithWidgetTests`, `LayoutServiceTests` addition.
- [ ] Commit `feat(profile): gallery card and viewer, recent scenes, often plays with`.

### Task 7: Visual check, gates, review, PR

- [ ] Seed a character with a gallery (icon + banner), a biography and two scenes; screenshot `/character/<name>` at 1280×800 and 390; compare with boards 25–26; `claude-show`.
- [ ] Format gate ×2; full bUnit; touched `SharpMUSH.Tests`, `SharpMUSH.Tests.ScenePlugin` and integration classes; `validate_resx.py`; responsive sweep.
- [ ] Fresh reviewer; fix pass; push `worktree-d1-phase-4-profile`; PR stacked on #1443.
