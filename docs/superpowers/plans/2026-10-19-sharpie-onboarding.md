# Sharpie Guided Journeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver optional, resumable player onboarding and a separate administrator preparation journey, using reviewed genre artwork without blocking on artwork production.

**Architecture:** Extend the existing public server-info presentation contract and account authentication flow. Persist progress through Mediator requests and a dedicated aggregate store implemented by Lightning; keep game configuration in the existing options writer. Render deterministic lessons in `/welcome` and a compact Play guide, with explicit user actions and capability-filtered links.

**Tech Stack:** Existing .NET SDK from `global.json`, C#, TUnit, Blazor WASM, bUnit, MudBlazor, Lightning/LMDB, existing browser JavaScript tests.

**Spec:** `docs/design/sharpie-onboarding.md`; latest approved artwork direction is six families and 42 themes per family. Read the updated spec before execution if it changes while artwork is produced.

## Global Constraints

- “Player onboarding comes first; new-administrator onboarding is a separate, required companion journey.”
- “No passwords, command text or message content are stored as onboarding progress.”
- “No image generation occurs during a user's visit.”
- “Do not expose or relax `api/mssp`, which is admin-only.”
- “Deliver implementation PRs and screenshots; do not merge or deploy without approval.”
- Six approved families: **Minimalist Mascot, Chibi Mascot, Anime Mascot (non-binary looks), Kemono Mascot, Pixel Mascot, Comicbook Mascot**. Stable IDs: `minimalist`, `chibi`, `anime`, `kemono`, `pixel`, `comicbook`.
- 42 themes × 6 families = 252 planned theme images, plus a neutral fallback for each style. Planned is not produced or approved. Each image is a newly generated individual PNG, no text, native transparent background, interacting with its genre/subgenre. Old atlas cuts are reference only.
- Preserve ink/mint/white identity and canonical M; full silhouettes, sufficient native resolution, no runtime generation, no full inventory precache. User no-art preference overrides admin artwork selection.
- Game name remains `Net.MudName`, logo remains `Cosmetic.PortalLogo`, genre/subgenre remain MSSP `GENRE`/`SUBGENRE`. Do not create competing identity fields.
- Existing permissions, import/claim lifecycle, package apply review and explicit starter-wiki action remain authoritative. Tutorial state grants no permissions.
- Use workspace `work/onboarding/` for scratch with `TMPDIR`, `TMP`, `TEMP` set there; never `/tmp`. C# tabs, Razor four spaces; formatter requires two converging passes.

## Review Focus

1. A delayed response for account A arriving after logout/login as B must never display or save A's progress into B (Task 4).
2. Two tabs saving the same revision must not silently erase dismissed state or completed lessons; stale writes return conflict and offer reload (Task 1).
3. A failed or stale feature read, removed package, or private wiki link must omit unavailable guidance without leaking titles (Task 2).
4. An existing formatted composer draft, IME composition, or repeated insertion click must preserve the draft and send nothing (Task 6).
5. Corrupt browser state, unknown genre/style and unfinished/rejected artwork must yield usable text guidance, preserving opt-out when recoverable (Tasks 3–4).

---

## Inspected baseline and file responsibilities

Baseline inspected: `ede117ea6`; working branch `codex/sharpie-onboarding-design`. This document plans changes only.

Existing seams verified:

- `SharpMUSH.Server/Controllers/ServerInfoController.cs` owns anonymous `api/server-info`; its response and the separate client response record must both be extended.
- `SharpMUSH.Client/Services/ServerInfoService.cs` memoizes successful responses for `MaxAge`, retries failures, and emits `Changed`; its `Differs` currently compares guests/name/logo/features only.
- `SharpMUSH.Library/Stores/IReadMarkerStore.cs`, its Mediator requests and `ReadMarkerHandlers.cs` demonstrate an explicitly uncached aggregate. New progress uses the same request/handler/store layering, not direct DB calls from controllers.
- `ISharpDatabase` composes store interfaces; `DatabaseRegistration.RegisterDatabaseProvider<TProvider>` explicitly registers each. Add the new store to both and to test/provider registration sites found by searching `IReadMarkerStore`.
- `LightningDatabase` uses `Store.Read`, serialized records and `Store.WriteAsync`; `Tables.All` discovers table definitions. Migration IDs currently stop at `0003_session_digest_authority`.
- `AccountController.AsAccountAsync` uses `BearerAccountResolver.ResolveAsync` and `SignedInAccount.Id`. Use this account-bound resolver for progress; don't invent claim names or accept an account ID in a request.
- `MsspController` requires `PortalPermission.ConfigAdmin`; `IConfigOptionWriter.UpdateAsync(Func<SharpMUSHOptions, SharpMUSHOptions>, CancellationToken)` performs locked option updates and notification. Its callback is not validated automatically.
- Setup currently has Resolving/Claim/SignIn/Import/Handlers/Packages/Done and query resume values `handlers`/`packages`. Import links already use these values.
- `PlayComposer.StartCommandAsync(string)` switches to Command **and replaces the draft**. It must not be called blindly by onboarding. `OnSend` is the live send boundary.
- `Play.razor` holds `_composer`; existing palette insertion already calls `StartCommandAsync`. Onboarding adds its own non-destructive insertion method without changing palette behavior.

New files are grouped by responsibility: shared guide records/catalog in Contracts; durable progress store and handlers; presentation and configuration endpoints; client guide state; focused Welcome/guide/art components; separate admin checklist. Each task lists its concrete paths below. New names are proposed contracts, not claims about existing APIs.

## Task 1: Durable versioned progress with account isolation

**Files:** Create `SharpMUSH.Contracts/Models/Portal/Onboarding/OnboardingProgress.cs`, `SharpMUSH.Library/Stores/IOnboardingStore.cs`, `SharpMUSH.Library/Queries/Database/GetOnboardingProgressQuery.cs`, `SharpMUSH.Library/Commands/Database/SaveOnboardingProgressCommand.cs`, `SharpMUSH.Implementation/Handlers/Database/OnboardingProgressHandlers.cs`, `SharpMUSH.Database.Lightning/Records/OnboardingProgressRecord.cs`, `SharpMUSH.Database.Lightning/LightningDatabase.Onboarding.cs`, `SharpMUSH.Server/Controllers/OnboardingController.cs`; modify `SharpMUSH.Library/ISharpDatabase.cs`, `SharpMUSH.Server/Registration/DatabaseRegistration.cs`, `SharpMUSH.Database.Lightning/Store/Tables.cs`, `SharpMUSH.Database.Lightning/Migration/LightningMigration.cs`. Tests: `SharpMUSH.Tests/Database/Lightning/OnboardingProgressTests.cs`, `SharpMUSH.Tests/Server/Controllers/OnboardingControllerTests.cs`, existing `MigrationTests.cs`.

**Interfaces:** Define `GuideJourney { Player, Administrator }`, `GuidePath { Discord, Mush, OtherRoleplay, Explore }`, `GuideImagery { Full, Reduced, None }`. `OnboardingProgress` carries `int SchemaVersion`, `int GuideVersion`, `long Revision`, `GuidePath Path`, `string[] CompletedStepIds`, `string[] SkippedStepIds`, `bool Dismissed`, `bool Active`, `GuideImagery Imagery`, `string? CurrentStepId`. No character or account identifier comes from its HTTP body. `IOnboardingStore.GetAsync(string accountId, GuideJourney journey, CancellationToken ct = default): ValueTask<OnboardingProgress>` returns empty version-1 progress at revision 0 when absent; `SaveAsync(string accountId, GuideJourney journey, OnboardingProgress progress, long expectedRevision, CancellationToken ct = default): ValueTask<Result<OnboardingProgress>>` atomically rejects revision mismatch. Matching Mediator query/command carry these arguments. Explicitly uncached, like read markers; no handler owns FusionCache.

- [ ] Write tests `NewAccountHasNoProgress`, `ReopenPreservesProgress`, `PlayerAndAdminAreIndependent`, `StaleRevisionIsRejected`, `MigrationIsIdempotent`, `CannotAddressAnotherAccount`. Assert revision increments once, stale save changes nothing, missing bearer is refused, and no endpoint accepts an account ID. Add two-tab close/complete conflict coverage.
- [ ] Run `dotnet run --project SharpMUSH.Tests -- --treenode-filter '/*/*/OnboardingProgressTests/*'` and controller tests; expect missing types or failing assertions before implementation.
- [ ] Implement records and aggregate using table `onboarding.progress`, key built from account ID and validated journey with an unambiguous encoding. Make existence/revision check and write one `Store.WriteAsync` operation. Register `0004_onboarding_progress_v1` (renumber if another migration has landed), idempotently recording schema initialization without manufacturing progress for accounts. Missing records are lazy defaults; unknown future versions fail explicitly, never overwrite. Run migration through existing startup migration lifecycle.
- [ ] Implement authenticated GET/PUT `api/onboarding/progress/{journey}` using `BearerAccountResolver`; PUT body has `Progress` and `ExpectedRevision`. Return 409 on stale revision, 400 for invalid version/path/step IDs, arrays over 64 IDs, duplicates or mutually completed/skipped IDs. Restrict administrator journey to `PortalPermission.ConfigAdmin`. Bound request size to 16 KiB. Use shared catalog IDs from Task 3 when available; initially define the ID constants in the shared progress file and move them with the catalog in that task.
- [ ] Run the three named test classes and existing migration tests; expect all pass, including reopen of a pre-onboarding world. Check all provider/test DI sites compile with the new interface.
- [ ] Commit only this task's files: `feat: persist account onboarding progress`.

## Task 2: Public presentation and safe capability projection

**Files:** Create `SharpMUSH.Configuration/Options/OnboardingOptions.cs`, `SharpMUSH.Contracts/Models/Portal/Onboarding/OnboardingPresentation.cs`, `SharpMUSH.Server/Services/OnboardingPresentationService.cs`, `SharpMUSH.Server/Controllers/OnboardingSettingsController.cs`; modify `SharpMUSH.Configuration/Options/SharpMUSHOptions.cs`, both ServerInfo files, and host service registration. Tests: existing `ServerInfoControllerTests.cs`, `ServerInfoServiceTests.cs`, configuration default/serialization tests; create `SharpMUSH.Tests/Server/Controllers/OnboardingSettingsControllerTests.cs`.

**Interfaces:** `OnboardingOptions` stores `Introduction`, `WelcomePage`, `RulesPage`, `CharacterGuidePage`, `ArtStyle`, `ArtOverrideId` as strings (empty = unset; style default `minimalist`). Add its backward-compatible default at the existing central options default boundary; old serialized options must deserialize without losing other fields. `OnboardingPresentation` exposes introduction, readable public guide links, chosen style/asset ID, and game-scoped opaque `GameId`. `OnboardingPresentationService.PublicAsync(CancellationToken): Task<OnboardingPresentation>` projects only anonymous-readable wiki links, without titles for hidden pages. `ServerInfoService.OnboardingAsync(): Task<OnboardingPresentation?>` returns null on failed reads. Add an optional trailing `Onboarding` property to both existing response records.

- [ ] Write tests `PrivateWikiTitleIsAbsent`, `FailedPresentationDoesNotAdvertiseLessons`, `ChangedPresentationRefreshesClient`, `LegacyOptionsPreserveIdentity`, `SettingsRequireConfigAdmin`, `IdentitySavePreservesUnrelatedMsspVariables`. Use a seeded private wiki page and assert neither slug nor title occurs in anonymous JSON.
- [ ] Run those tests; expect failures before the presentation extension exists.
- [ ] Add bounded options (introduction 1000 characters, links 256 each, style 32, override ID 128); validate known style/asset identifiers, plain introduction text, and internal wiki identifiers, never arbitrary executable URLs. Add authenticated settings GET/PUT under `api/onboarding/settings` with `PortalPermission.ConfigAdmin`, `IConfigOptionWriter.UpdateAsync`, and audit recording. Request includes existing name/genre/subgenre plus guide options; update only their corresponding fields, preserving all other MSSP values. Extend the Task 1 migration with a durable random `GameId` in meta state (idempotent put-if-absent). Add `IOnboardingStore.GetGameIdAsync(CancellationToken ct = default): ValueTask<string>`, `GetOnboardingGameIdQuery : IQuery<string>` in `SharpMUSH.Library/Queries/Database/GetOnboardingGameIdQuery.cs`, and its handler beside the progress handlers; read identity through this uncached Mediator request, not filesystem paths or process IDs. A deployed earlier migration must receive a new migration ID instead of rewriting history. Browser storage scopes by origin plus this ID.
- [ ] Build public projection via existing wiki authorization services with an anonymous principal; add authenticated `api/onboarding/capabilities` to `OnboardingController` for character-visible curated command metadata. Define entries as `Id`, `Command`, `Audience`, `HelpUrl`, `PackageId`, `InstalledVersion`; return only entries proven by current installed package version AND current character access. Start with core lessons and an empty optional-command list unless a package-specific adapter has evidence. Broad `GameFeatures` may show general scene/wiki/Discord explanations but must never synthesize `+scene`/`+help` or dice commands. Do not query admin inventory from the client.
- [ ] Extend client `Differs` to compare the bounded presentation structurally, preserve the existing retry/stale-cache behavior, and treat failed onboarding/capability reads as unknown. Run server/client tests, options default tests, and remove/disable package tests; expect unavailable lessons omitted, other public fields unchanged.
- [ ] Commit: `feat: expose safe onboarding presentation and settings`.

## Task 3: Lesson catalog and production artwork selection

**Files:** Create `SharpMUSH.Contracts/Models/Portal/Onboarding/GuideCatalog.cs`, `SharpMUSH.Contracts/Models/Portal/Onboarding/GuideArtCatalog.cs`, `SharpMUSH.Client/Components/Onboarding/GuideArt.razor`, `SharpMUSH.Client/Components/Onboarding/GuideArt.razor.css`, `SharpMUSH.Client/wwwroot/images/sharpie/onboarding/catalog.json`; modify `SharpMUSH.Client/Resources/SharedResource.resx` and `SharpMUSH.Client/wwwroot/service-worker.js` only if its current caching rules would include the new inventory. Tests: `SharpMUSH.Tests.BUnit/Services/GuideCatalogTests.cs`, `SharpMUSH.Tests.BUnit/Components/Onboarding/GuideArtTests.cs`.

**Interfaces:** `GuideCatalog.PlayerSteps` has stable IDs `place`, `sign-in`, `character`, `world`, `useful-action`, `speak-act`, `continue`; admin IDs `identity`, `joining`, `rules`, `capabilities`, `walkthrough`, `invite`. `GuideLesson` contains ID, localization resource keys per path, primary action kind and optional required feature ID. `GuideArtCatalog.Select(string? overrideId, string? subgenre, string? genre, string style): GuideArtSelection` returns an optional approved local URL and dimensions; absence means compact approved brand SVG or no image. Catalog rows contain ID, family, theme ID, genre/subgenre mapping, file, width/height, native-alpha provenance, status (`planned`, `review`, `approved`, `rejected`) and review record. Only approved rows with present files can be returned.

- [ ] Write `AllFourPathsHaveStableSteps`, `CoreCommandExamplesHaveAudiences`, `UnknownThemeFallsBackWithinStyle`, `PlannedRowsNeverProduceUrls`, `NoArtMakesNoImageRequest`, `MissingImageRetainsText`. Assert selection precedence override → supported subgenre → supported genre → same-style neutral → approved compact brand SVG/no image. Do not silently switch art family when its neutral is unfinished.
- [ ] Run the new catalog/component tests; expect missing catalog/component failure.
- [ ] Implement localized content from the approved spec, including MUSH “what's new”, forum/tabletop optional explanations, account/character distinction and all seven steps. Catalog examples: `look`, `help`, `help say`, `inventory`, `who`, `say Hello!`, `pose smiles.`, `page Name=Hello`; state each audience and distinguish local preview from live output. Check actual command attributes/help and verify runtime examples during Task 8; omit unverified package examples.
- [ ] Import the artwork producer's **manifest**, retaining its actual 42 theme IDs and six family IDs. Do not invent replacement theme names or declare all 252 files shipped. If the production manifest is not yet available, ship an empty approved collection and documented pending inventory; add the manifest when delivered. Add six neutral rows as planned until individually approved. Preserve producer provenance and visual review evidence; publish only approved assets, never rejected atlas cuts. Use `object-fit: contain`, decorative empty alt text and actual intrinsic dimensions. Keep one selected representation for the journey; loading failure removes the image without changing lessons.
- [ ] Run tests and inspect network loading: no artwork preload loop or full inventory precache; no-art mode fetches zero mascot PNGs. Artwork acceptance requires white/near-black/theme-color views at actual size, full anatomy/silhouette, canonical M and mint consistency. Show review images using `Codex-show`.
- [ ] Commit reviewed catalog/UI files and only approved assets: `feat: add guided lesson and artwork catalogs`.

## Task 4: Resumable client state and anonymous continuation

**Files:** Create `SharpMUSH.Client/Services/OnboardingService.cs`, `SharpMUSH.Client/Services/OnboardingBrowserState.cs`; modify `SharpMUSH.Client/Program.cs`. Consume existing `BrowserStorage`, account-auth changes and API result handling rather than a second authentication layer. Tests: `SharpMUSH.Tests.BUnit/Services/OnboardingServiceTests.cs`.

**Interfaces:** `OnboardingService.LoadAsync(GuideJourney journey): Task`, `ChoosePathAsync(GuidePath): Task`, `CompleteStepAsync(string): Task`, `SkipStepAsync(string): Task`, `DismissAsync(): Task`, `SetImageryAsync(GuideImagery): Task`, `AcceptAnonymousContinuationAsync(): Task`, `DeclineAnonymousContinuationAsync(): Task`, `ResetAccountState(): void`; expose immutable current progress, load/error state and `Changed`. Browser record key is versioned and scoped by origin/GameId/journey; signed-in progress goes to Task 1 API. Store only progress and whether continuation was offered for this account, never tokens or command drafts.

- [ ] Write `LateResponseCannotCrossAccount`, `ReloadRetainsDismissal`, `GuideUpgradeRetainsOptOut`, `AnonymousContinuationRequiresConsent`, `DecliningDoesNotReplaceAccountProgress`, `CorruptStorageDoesNotTrapGuide`, `ConflictRequiresReload`, `CharacterSwitchReevaluatesCapabilities`. Assert explicit consent merges completed/skipped sets without resetting account preference; completed wins over skipped; existing account dismissal/path/image preference wins. Declining consumes the once-only prompt for that account/game. Unknown future browser versions remain untouched.
- [ ] Run the service test class; expect missing service failure.
- [ ] Implement generation/cancellation guards for account-bound requests, serialized saves with expected revisions, visible recoverable errors and Retry. Reload 409 conflicts before retrying; never automatically overwrite newer server preferences. On logout/account switch clear progress and character-specific capability state immediately. Preserve account learning when a character changes, but reset current character readiness and reload capabilities. Version migration retains dismissal and image preference; unknown steps don't grant completion to new steps.
- [ ] Run service tests with controlled delayed HTTP responses and browser-storage exceptions. Expect no cross-account writes, no reopen after Close and no false completion from route visits or insertion.
- [ ] Commit: `feat: resume optional guide progress safely`.

## Task 5: Player-first entry, welcome and authentication handoff

**Files:** Create `SharpMUSH.Client/Pages/Welcome.razor`, `SharpMUSH.Client/Pages/Welcome.razor.css`, `SharpMUSH.Client/Components/Onboarding/GuidePanel.razor`, `SharpMUSH.Client/Components/Onboarding/GuidePanel.razor.css`; modify `Pages/Home.razor`, `Pages/Login.razor`, `Pages/CharacterCreate.razor`, `Components/HelpDrawer.razor`, resource keys and existing `Pages/OnboardingRoutingTests.cs`. Tests: `SharpMUSH.Tests.BUnit/Pages/WelcomeTests.cs`.

**Interfaces:** `/welcome` accepts a local `returnUrl`; default `/play`. `GuidePanel` receives current lesson/progress and callbacks for primary action, Back, Skip, Close, path/image changes. It never dispatches game commands. Use existing roster create/claim/select APIs and existing login return-target validation; do not implement a parallel roster workflow.

- [ ] Write `HomeOffersButDoesNotOpenGuide`, `DirectPlayDoesNotRedirectToWelcome`, `FourPathsPreserveReturnTarget`, `ExternalReturnUrlIsRejected`, `RosterFailureShowsRetry`, `ClaimPathDoesNotCreateDuplicate`, `ReadingGuideWorksAnonymously`. Assert “Let me explore” closes proactive guidance and navigates immediately; MUSH route offers the short delta view.
- [ ] Run Welcome and onboarding routing tests; expect the new UI assertions to fail.
- [ ] Implement optional home/login links with exact labels from the spec; add Help entries “Getting started with Sharpie” and “Change my guide”. Welcome leads with game identity and readable guide links, then familiarity choices. Use one primary action per lesson and Back/Skip/Close. Show only enabled authentication methods using existing login UI; preserve local return destination through signup, external auth and create/claim. Distinguish loading/empty/error rosters; errors retain position and expose Retry. A disabled feature has no action, and missing intro/rules links do not produce invented lore or acknowledgement gates.
- [ ] Run tests for all four paths, returning/registration/external-login handoff, path changes and no-art mode. Verify keyboard focus returns to the opening link after Close and lesson headings announce predictably without stealing composer focus.
- [ ] Commit: `feat: add optional player welcome journey`.

## Task 6: Contextual Play lessons with safe local practice

**Files:** Modify `SharpMUSH.Client/Pages/Play.razor`, `Play.razor.cs`, `Play.razor.css`, `Components/Play/PlayComposer.razor`, `Components/Play/PlayGate.razor`; create `Components/Onboarding/GuidePractice.razor`; extend `PlayComposerTests.cs` and create `SharpMUSH.Tests.BUnit/Components/Onboarding/PlayGuideTests.cs`.

**Interfaces:** Add `PlayComposer.TryInsertGuideCommandAsync(string line): Task<bool>`: succeeds only when draft is empty and the composer is connected; switches to raw Command through existing `StartCommandAsync`, returns false otherwise, never calls `OnSend`. The guide passes only catalog `look`. Practice completion is an explicit callback from local practice, never a live-output heuristic. Stable `data-guide` anchors identify composer, view switch, current character, connection status, Here/Exits and help only where rendered.

- [ ] Write `PracticeNeverCallsSend`, `InsertLookSelectsCommandWithoutSending`, `ExistingFormattedDraftIsUnchanged`, `RepeatedInsertDoesNotReplaceDraft`, `DisconnectedInsertIsRefused`, `HiddenAnchorHasPlainFallback`, `MobileGuideDoesNotCoverComposer`. Assert insertion does not mark completion and local practice has an explicit “Local practice — not sent to the game” label.
- [ ] Run composer and Play guide test classes; expect new method/UI assertions to fail.
- [ ] Implement safe insertion and local practice; on false result explain that the user can finish/clear their draft first. Do not clear or stash drafts, switch away from an active IME edit, bind extra send shortcuts, or auto-send on reconnection. Explain Say/OOC/Emit literal handling and raw Command evaluation using the existing composer behavior. Speech/page examples remain text only, with audiences stated before practice. Enable explicit “I already know this” or practice completion; a clicked link or inserted command is not evidence of success.
- [ ] Place guide alongside Play on desktop and in-flow on mobile, with a full-page guide link when space is insufficient. Follow visibility/render state rather than coordinate spotlights. Finish with “You're ready to explore” and capability-filtered next steps. Route/reconnect changes must not reactivate dismissed guidance.
- [ ] Run component tests and existing client JavaScript suite if wwwroot changes. Browser-check 320 CSS px, 200% zoom, keyboard, reduced motion, light/dark and Story/Terminal. Record live `look` and `help` performed by the tester; verify insertion/practice caused zero sends and explicit Send caused one.
- [ ] Commit: `feat: guide players through safe first actions`.

## Task 7: Separate administrator identity and preparation journey

**Files:** Modify `SharpMUSH.Client/Pages/Setup.razor`, `Setup.razor.css`, `SharpMUSH.Client/Pages/Admin/Dashboard.razor`; create `SharpMUSH.Client/Components/Onboarding/GameIdentityForm.razor`, `SharpMUSH.Client/Pages/Admin/Onboarding.razor`, `Onboarding.razor.css`. Extend `OnboardingRoutingTests.cs`; create `SharpMUSH.Tests.BUnit/Pages/AdminOnboardingTests.cs` and `SetupIdentityTests.cs`.

**Interfaces:** Reuse Task 2 settings endpoint; `GameIdentityForm` accepts loaded settings and an explicit save callback. Route `/admin/onboarding` requires configuration permission and uses `GuideJourney.Administrator`. Identity step sits after import resolution and before handlers; retain existing `?step=handlers`/`packages` compatibility and add `?step=identity`. No new setup-complete flag, import endpoint or admin-claim ceremony.

- [ ] Write `ImportedIdentityIsPrefilled`, `IdentityDoesNotSaveOnRender`, `InterruptedIdentityCanResume`, `ExistingImportReturnStillResolves`, `ExistingAdminDoesNotReclaimSetup`, `PlayerTourDoesNotCompleteAdminChecklist`, `FinishDoesNotPublishOrInstall`. Verify public preview carries an explicit limitation: ordinary-player permissions still need testing.
- [ ] Run new setup/admin tests plus existing onboarding routing tests; expect missing identity/checklist UI failures.
- [ ] Insert identity UI after import/skip resolution with explicit Save/Continue and defer behavior. Prefill `MudName`, introduction and MSSP genre/subgenre, use Task 3 style preview, and preserve imported values unless changed. Update step numbering and import return handling without overriding explicit older resume routes. Resume via saved admin progress and existing wizard state; an administrator may return after finishing setup.
- [ ] Implement independently editable/deferable six-item checklist: identity, joining, rules, capabilities, player walkthrough, ready to invite. Link to existing sign-in/registration, theme, wiki, package review/apply and starter-wiki surfaces. Preview all familiarity routes without impersonation and without writing player progress. Final summary lists working links and deferred items; it does not publish, change registration, install packages or send invites.
- [ ] Run tests for fresh setup, imported world, interrupted/reopened setup, existing admin and forbidden requests. Walk the configured site with an ordinary player account before marking the walkthrough verified; preview alone cannot check this item.
- [ ] Commit: `feat: help administrators prepare player onboarding`.

## Task 8: Runtime evidence, documentation and review handoff

**Files:** Create `docs/guides/getting-started.md`, `docs/guides/sharpmush-for-mush-players.md`, `docs/guides/onboarding-administration.md`, `docs/guides/onboarding-art.md`, `docs/validation/sharpie-onboarding.md`; update existing front-page design's automatic-welcome-modal wording and relevant docs navigation. Put screenshots in the repository's established review-artifact location, not scratch.

- [ ] Add a requirement-to-evidence checklist covering every spec validation bullet. Each row records scenario, automated test, runtime result and screenshot/reference, plus explicit outstanding artwork acceptance. Keep the 252 planned theme images separate from approved delivery count.
- [ ] Run two whitespace formatter passes for changed C# project folders, then `dotnet build`, focused changed-service/controller suites and `dotnet run --project SharpMUSH.Tests.BUnit`; run the actual package's existing Node test command when wwwroot is changed. Set temporary environment variables to workspace scratch first. Record actual commands/results; do not call tests passed based on a build alone.
- [ ] Run browser scenarios: all four familiarity routes desktop/mobile light/dark; signup/returning/external login; create/claim/switch; reload/reconnect/logout/second account; version upgrade; private wiki; capabilities failing/removed; missing art/unknown subgenre; hidden controls; keyboard/screen-reader semantics/zoom/reduced-motion/no-art. Inspect requests to verify no example sends and no inventory download. Capture screenshots and display via `Codex-show`.
- [ ] Run new-admin setup/import/resume/existing-admin cases and ordinary-player verification. Check command examples against live engine behavior, including literal Say/OOC/Emit versus Command; record limitations of customized game commands. Correct failures before handoff.
- [ ] Write concise player quickstarts, verified command reference, MUSH differences, admin settings and art curation docs from the shared catalog; document fallback, provenance, image acceptance and explicit package capability evidence. Remove stale automatic-modal guidance.
- [ ] Review diff, checks and screenshots; create implementation PR(s) in SharpMUSH only, attach them to the chat, and carry through CI/review findings. Do not merge or deploy. Commit docs/evidence: `docs: document and validate guided onboarding`.

## Dependencies and completion boundary

Tasks 1–2 establish persistence/presentation; Task 3 can proceed alongside them. Task 4 depends on 1–3; Task 5 depends on 4; Task 6 depends on 5. Validate the player path before Task 7 polish; Task 8 closes both journeys. Artwork production proceeds independently, feeding Task 3 only approved assets. The software must work with no produced theme assets. Completing the software does not mean the separate 252-image production effort is complete.

## Self-review completed

- Coverage: entry/familiarity/common lessons/experienced route are Tasks 3, 5–6; artwork selection and production boundaries Task 3; setup/admin Task 7; state/security Tasks 1–4; accessibility and runtime evidence Tasks 5–8; supporting docs and PR handoff Task 8.
- Type consistency: progress is account-bound with separate journey keys; all UI consumers use the Task 1 enums/record and Task 4 service; server-info remains its existing dual-record contract. New interfaces are explicitly marked as planned.
- Review focus: all five cases above have named tests in owning tasks. Added revision conflict and safe composer insertion after examining actual implementation seams.
- Scope: no chatbot, package installer, alternate authentication mechanism, permission bypass or automatic message sends. No product code changed while preparing this plan.
- Execution inputs still needed: the artwork producer's final manifest/reviewed files, runtime test credentials/configured external provider, and actual SDK/runtime availability. These block their evidence or asset acceptance only; they do not justify inventing completed artwork or skipping required journeys.
