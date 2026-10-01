# D1 Phase 6 — Play UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/play` matches boards 01–11: the Play page sidebar, the room banner from `room.info`, the scene card with a Story | Terminal switch and focus mode, the Story view of the scene's poses (portraits, name colours, the OOC band), Here and Exits as components with every exit state, the character sheet drawer and portrait viewer, the composer, and the mobile layout. Quick actions go.

**Architecture:** Phase 5's typed `RoomState` (`IOobChannelStore.Room`, `RoomChanged`) drives the banner, Here and Exits. The Story view is a reusable `SceneStory` component extracted from `SceneLive` (REST backlog + scene hub events, now with `ActorObjId`); Play learns its scene from `room.info.scene`. The Play sidebar goes into the shell's page-sidebar slot through `SectionShell` (Key "play"); channels and pages come from an `ICommFeed` interface whose Phase 6 implementation is empty (README §7.3; Phase 7 fills it). Portraits and name colours come from occupant rows (`image`, `color`) and the directory.

**Tech Stack:** .NET 11, Blazor WASM, MudBlazor 9, TUnit + bUnit.

**Spec:** `docs/design/d1/README.md` §4.10, §4.11, §5, §7.1–7.3; boards 01–11; spec `docs/superpowers/specs/2026-09-29-image-attributes-and-oob-v2-design.md` §6 (URL policy at render time).

**Stacks on:** Phase 1 shell (#1446) merged with Phase 5 Play data (#1445).

## Global Constraints

- Exit alias keys never fire while focus is in a text input, textarea, select or editable element.
- Dark exits are never sent, so the client never invents one; every exit state comes from the payload.
- A payload without `v` (v1) still renders: names, and `cmd` where present.
- Image URLs render only through `ImageUrlPolicy`.
- ui-patterns §14; player-facing strings in all 16 resx.

## Review Focus

1. A v1 room (no images, no states) renders tiles with icon fallbacks and working `cmd`s (Task 4).
2. Typing "n" in the composer does not take the north exit (Task 4).
3. A pose from someone not in the directory, without `ActorObjId` or an image, renders initials in their colour or the default (Task 2).
4. Leaving a scene with a `confirm` exit asks first and does nothing on cancel (Task 4).
5. Outside a scene there is no Story view; Terminal shows the raw stream and the switch is not offered (Task 3).

---

### Task 1: Play sidebar and comm feed seam
`Components/Play/PlaySidebar.razor` (+css): "Play · ● Connected as {name}", In scene (image row with cast), Channels (`#` rows, unread pill, bold when unread), Pages (avatar or stacked pair, unread pill), collapsed strip; `Services/ICommFeed.cs` + `EmptyCommFeed`; Play wraps in `SectionShell Key="play"`. Tests `PlaySidebarD1Tests`.

### Task 2: SceneStory (extracted from SceneLive) and the Story rows
`Components/Scenes/SceneStory.razor` (backlog, hub join/leave, pose/edit/delete/move); `Components/Scenes/StoryPose.razor` (44px portrait radius 12 from `ActorObjId` → directory image or occupant image; name in colour, time, 15px/1.6 body; `OocBand` for tag `ooc`; other participants' names as mentions). `SceneLive` uses `SceneStory`. Tests `SceneStoryTests`, `StoryPoseTests`, SceneLive tests stay green.

### Task 3: Room banner and scene card
`Components/Play/RoomBanner.razor` (GlassBanner from `RoomInfo`: image+focal, name, "area · N here · N exits", Description toggle opens the banner to 200px with `desc.text`, Minimise), `Components/Play/SceneCard.razor` (title = scene title or room name, sub-line per view, Story | Terminal radiogroup only in a scene, focus button hides the pagebar and banner; terminal settings menu kept for width/clear). Tests.

### Task 4: Here and Exits
`Components/Play/HereCard.razor`, `ExitsCard.razor`, `ExitTile.razor`, `ExitPreview.razor`: characters as portrait tiles (status word, you ringed), objects as square thumbnails after characters; clicking a character with `profile` opens the sheet, an object runs its `cmd`; exits as image tiles with every state (open, occupied count, locked + hint, closed door action, leaves scene → confirm, no image icon), keycap = first alias, alias keys via a document listener that ignores editable targets, hover/focus preview (desc, N there, Go, Look). Tests `HereCardTests`, `ExitsCardTests`, node test for the alias-key guard.

### Task 5: Character sheet drawer
`Components/Play/CharacterSheet.razor`: right overlay; full-height portrait, Full image → `ImageViewer`; name colour, role, pills; Look · Page · Mail · Profile; description; Details (profile schema compact); gallery strip. Tests.

### Task 6: Composer
`Components/Play/PlayComposer.razor` in a `BottomBar`: TypeChips Pose / Say / OOC / Command, textarea, Send; commands `pose`, `say`, `ooc`, raw, through `MushComposeEncoder`; Enter sends, Shift+Enter newline. Terminal view keeps GlobalTerminal's own input. Tests.

### Task 7: Play page composition and mobile
`Pages/Play.razor` rebuilt from the parts; Quick actions removed; mobile: compact banner, Story, a bottom Room sheet (Here/Exits tabs, exits as rows with keycaps), Play's own tabs Scene · Room · #Channels · Pages. Tests `PlayPageD1Tests`.

### Task 8: Visual check, gates, review, PR
