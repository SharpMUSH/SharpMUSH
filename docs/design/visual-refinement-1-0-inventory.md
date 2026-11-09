# First-pass source inventory

Historical audit notes from the initial pass. Pending checks and observations here are superseded by the final [refinement report](visual-refinement-1-0.md); they are retained to show the component inventory and initial evidence boundaries.

## Detailed source coverage

# Player, widget, builder and schema coverage ledger

Baseline: current main cfa5af9013; source audit and focused implementation in the shared checkout. **No browser screenshots or runtime interaction results were produced by this agent.** Parent owns runtime acceptance and all build/test execution. This ledger records source coverage, not a claim that every state was exercised.

## Player route families

| Family | Routes/source coverage | State |
|---|---|---|
| Home/onboarding/auth | Home, Welcome, Login, Register, Setup, Account; `/`, `/welcome`, `/login`, `/register`, `/setup`, `/account` | Source audited; credential autofill hints added; runtime short viewport/keyboard required |
| Characters | directory, new/claim, profile, gallery, character scenes | Source audited; directory retry preserves filter; gallery and long profile need visual coverage |
| Scenes | archive/filter/calendar, active, detail, live | Source audited; existing current-main loading, draft sync and mobile composer retained; keyboard/long-pose runtime acceptance pending |
| Mail | folder list, split reading panel, compose, message detail | Source audited; loading ARIA and long-text containment refined; composer draft persistence intentionally not expanded |
| Play | terminal/story, guide panel, room/occupants/exits, responsive aside | Source audited; native socket/keyboard and OOB behavior require runtime validation |
| Help | index, public topic, admin topic | Source audited; index retry preserves filter; existing skeletons retained |
| Wiki | index, collection, query, recent, article/edit/history/diff | Source audited; diff mode selection accessible; editor/history/long rich content need runtime coverage |

## Widget component groups (all 17 Razor widgets)

| Group | Components | Source findings and acceptance targets |
|---|---|---|
| Home statistics/activity | StatsWidget, OnlineCharactersWidget, ActiveSceneWidget, RecentWikiActivityWidget | Stats distinguishes unavailable from zero and retries; Online distinguishes failure/empty. Active's zero ICU text is just 'Live now', so no false numeric count fix needed. Recent wiki previously mapped service failure to empty; changed to result API and unavailable state, with regression. Screenshot both main/aside variants and delayed/error/empty data. |
| Discovery/onboarding | CharacterDirectoryWidget, QuickstartWidget, QuickLinksWidget | Directory search/categories and explicit failure; quickstart account/character-specific offers; quick links sidebar/chip variants and admin-only empty configuration prompt. Test long labels and anonymous/member views. |
| Profile | CharacterGalleryWidget, OftenPlaysWithWidget, RecentScenesWidget | Gallery warning covers and editable viewer entry points; partners and recent scenes distinguish missing data. Profile context and character switch invalidation exist. Test empty/warned gallery, narrow aside, content load. |
| Wiki/content | WikiBodyWidget, WikiIndexWidget, WelcomeTextWidget | Body remounts per address and keeps edit mode; permissions govern missing-page offer. Index uses pinned collection/cards/search. Welcome supports guest visibility and Markdown. Test long tables/headings, failed index, embedded editing. |
| Live room | HereWidget, ExitsWidget | Delegate to HereCard/ExitsCard; bind room OOB events and dispose subscriptions; exit keys limited to Play context. Runtime room event and mobile sheet acceptance pending. |
| Extensibility/layout | SchemaWidget, SpacerWidget | Schema widget loading remains a small circular indicator; host routes/OOB data delegate to schema host. Spacer is intentionally aria-hidden. Verify narrow column and application unavailable states. |

## Builder/application surfaces

| Surface | Source coverage | Concrete findings / pending checks |
|---|---|---|
| AppBuilder `/build/apps` | route layout, steps, templates/open, settings/outline/preview panes, value mapping/save, responsive CSS | Mobile switches panes; desktop preview has 390px width but no named page container of its own. Existing schema renderer queries outer page, so 'phone' preview can retain desktop declared columns. Parent owns fix. |
| BuilderOutline | tree rows, keyboard buttons, move/reorder, issue links | Move buttons remain available besides drag, hover tools become visible at narrow tier. Test deep nested labels and keyboard ordering. |
| BuilderElementSettings | field/display settings, content/rules/click/layout tabs and TabKey handler | Arrow/Home/End handler changes selected tab/tabindex but does not focus the new tab; repeated arrow key events start from old focused tab and can stall. Fixed after reporting: focused element now follows selection; BuilderElementSettingsTests checks repeated arrow navigation and focus calls. |
| BuilderAddPalette | searchable grouped popup and bounded popup CSS | Width min(320px, viewport-32px), bounded scrolling and labeled trigger/dialog. Verify actual focus handling through shared PopoverMenu. |
| BuilderPreview | width radio controls, real/sample data, same schema host | Preview-specific responsive containment needed as above. Radio buttons all remain tab stops and lack arrow navigation; prefer native radio or full radio keyboard semantics in future refinement. |
| HostedApplication | bootstrap, iframe, host consent/plan panels, async lifecycle | Added localized loading skeleton/status until bootstrap available; action rows now wrap. Existing sandbox and authorization behavior unchanged. Focus transfer into host consent still needs runtime evaluation. |
| DynamicApplication `/apps/{Slug}/{**Rest}` | registered/component/hosted/schema dispatch, missing/access/character states | Uses explicit visible error messages, but alert divs lack alert/status semantics. Installed package data and routes must be tested; registration inventory is not proof of rendered package behavior. |

## Schema components

| Group | Source coverage | Findings / validation |
|---|---|---|
| Host/state | SchemaApplicationHost; supporting state/patch/click/rendering/value models inventoried | Host preserves form edits while refreshing views; action/error/redirect flow and character/location retirement reviewed. State/helper files not exhaustively algorithm-audited; beyond visual scope. |
| Read-only views | SchemaViewRenderer, SchemaDisplay | Visible-field filtering; auto/declared grids; Markdown/image/keyvalue/table/timeline/stats/chips/graph/buttons; text wrapping and row controls. Named page query creates nested preview issue. Tables, rich text and multi-column values require visual fixtures. |
| Inputs | SchemaFieldInput | boolean/slider/number/textarea/MString/select/multiselect/list/rows/radio/date/text; row actions labeled. SchemaFormRenderer belongs to admin agent and is excluded from this ownership. |
| Graphs | SchemaGraph, SchemaGraphEditor; layout/draft/guidance inventoried | SVG read view plus link hit targets; editor map/outline, zoom/add/node/edge controls. Real graph fixture, keyboard nodes, mobile pan/scroll and long labels unverified. |
| Dialog/diagnostic | SchemaDialog, SchemaUnknownKind | Fetch/error/cancel semantics; unknown kinds are admin-only diagnostics. Dialog loading has bare circular indicator; parent can unify loading later. |

## Installed/example package grouping

Manifest inventory identifies application examples: bboards-app (boards), jobs-app, chargen-app, duties-app, highlights-app, notifications-app, relationships-app and relationships-card; hosted UI: builder-graph; compiled-plugin example: hello-ui. Companion configuration/help packages and world-map/world-grid packages are separate content/configuration families. **These manifests were inventoried; their package-specific flows were not loaded or visually tested by this agent.** To claim package coverage, render boards list/thread, jobs list/detail/form, chargen workflow, duties list/action, highlights collection/detail, notifications, relationships view/card, and builder-graph host canvas/consent. Third-party or game-installed components require the runtime registry inventory from parent; repository manifests alone cannot enumerate them.

## Changes and checks

Initial tests: CharactersPageD1Tests, HelpFailureTests, LoginPageRenderTests, WikiHistoryLocaleTests. Extended tests: HostedApplicationTests (delayed bootstrap), WikiAsideWidgetsTests (recent activity failure), BuilderElementSettingsTests (repeated arrow selection and focus). All execution delegated to parent to avoid concurrent builds. `git diff --check` passed after changes. Existing localized keys reused except Retry, requested from parent.

## Exact extended component inventory

Files below were enumerated. Razor presentation markup and relevant layout styles were reviewed; supporting C# domain/model helpers are inventory-only unless named in the findings above.

### Widgets

- `SharpMUSH.Client/Components/Widgets/ActiveSceneWidget.razor`
- `SharpMUSH.Client/Components/Widgets/ActiveSceneWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/CharacterDirectoryWidget.razor`
- `SharpMUSH.Client/Components/Widgets/CharacterDirectoryWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/CharacterGalleryWidget.razor`
- `SharpMUSH.Client/Components/Widgets/CharacterGalleryWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/ExitsWidget.razor`
- `SharpMUSH.Client/Components/Widgets/HereWidget.razor`
- `SharpMUSH.Client/Components/Widgets/OftenPlaysWithWidget.razor`
- `SharpMUSH.Client/Components/Widgets/OftenPlaysWithWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/OnlineCharactersWidget.razor`
- `SharpMUSH.Client/Components/Widgets/OnlineCharactersWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/ProfileCharacter.cs`
- `SharpMUSH.Client/Components/Widgets/QuickLinksWidget.razor`
- `SharpMUSH.Client/Components/Widgets/QuickLinksWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/QuickstartWidget.razor`
- `SharpMUSH.Client/Components/Widgets/QuickstartWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/RecentScenesWidget.razor`
- `SharpMUSH.Client/Components/Widgets/RecentScenesWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/RecentWikiActivityWidget.razor`
- `SharpMUSH.Client/Components/Widgets/RecentWikiActivityWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/SchemaWidget.razor`
- `SharpMUSH.Client/Components/Widgets/SpacerWidget.razor`
- `SharpMUSH.Client/Components/Widgets/StatsWidget.razor`
- `SharpMUSH.Client/Components/Widgets/StatsWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/WelcomeTextWidget.razor`
- `SharpMUSH.Client/Components/Widgets/WelcomeTextWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/WikiBodyWidget.razor`
- `SharpMUSH.Client/Components/Widgets/WikiBodyWidget.razor.css`
- `SharpMUSH.Client/Components/Widgets/WikiIndexWidget.razor`
- `SharpMUSH.Client/Components/Widgets/WikiIndexWidget.razor.css`

### Builder

- `SharpMUSH.Client/Components/Builder/BuilderAddPalette.razor`
- `SharpMUSH.Client/Components/Builder/BuilderAddPalette.razor.css`
- `SharpMUSH.Client/Components/Builder/BuilderDraft.cs`
- `SharpMUSH.Client/Components/Builder/BuilderElementSettings.razor`
- `SharpMUSH.Client/Components/Builder/BuilderOutline.razor`
- `SharpMUSH.Client/Components/Builder/BuilderOutline.razor.css`
- `SharpMUSH.Client/Components/Builder/BuilderPreview.razor`
- `SharpMUSH.Client/Components/Builder/BuilderSkeleton.razor`
- `SharpMUSH.Client/Components/Builder/BuilderSkeleton.razor.css`
- `SharpMUSH.Client/Components/Builder/BuilderTemplates.cs`

### Applications

- `SharpMUSH.Client/Components/Applications/HostedApplication.razor`

### Schema

- `SharpMUSH.Client/Components/Schema/GraphDraft.cs`
- `SharpMUSH.Client/Components/Schema/GraphGuidance.cs`
- `SharpMUSH.Client/Components/Schema/SchemaApplicationHost.razor`
- `SharpMUSH.Client/Components/Schema/SchemaApplicationState.cs`
- `SharpMUSH.Client/Components/Schema/SchemaClick.cs`
- `SharpMUSH.Client/Components/Schema/SchemaDialog.razor`
- `SharpMUSH.Client/Components/Schema/SchemaDisplay.razor`
- `SharpMUSH.Client/Components/Schema/SchemaDisplay.razor.css`
- `SharpMUSH.Client/Components/Schema/SchemaFieldInput.razor`
- `SharpMUSH.Client/Components/Schema/SchemaFieldInput.razor.css`
- `SharpMUSH.Client/Components/Schema/SchemaFormModel.cs`
- `SharpMUSH.Client/Components/Schema/SchemaFormRenderer.razor` — admin agent ownership
- `SharpMUSH.Client/Components/Schema/SchemaGraph.razor`
- `SharpMUSH.Client/Components/Schema/SchemaGraph.razor.css`
- `SharpMUSH.Client/Components/Schema/SchemaGraphEditor.razor`
- `SharpMUSH.Client/Components/Schema/SchemaGraphEditor.razor.css`
- `SharpMUSH.Client/Components/Schema/SchemaGraphLayout.cs`
- `SharpMUSH.Client/Components/Schema/SchemaPatch.cs`
- `SharpMUSH.Client/Components/Schema/SchemaRendering.cs`
- `SharpMUSH.Client/Components/Schema/SchemaUnknownKind.razor`
- `SharpMUSH.Client/Components/Schema/SchemaValues.cs`
- `SharpMUSH.Client/Components/Schema/SchemaViewRenderer.razor`
- `SharpMUSH.Client/Components/Schema/SchemaViewRenderer.razor.css`



# Administration, settings and package coverage

Baseline: current main cfa5af9013, branch codex/visual-refinement-1-0. This is an inventory plus focused source/behavior audit, not a claim that every listed route was visually accepted. Parent coordinates full portal runtime coverage.

## Completed changes and evidence

- Admin accounts staff-recovery card now uses the existing padded accounts-body wrapper; source confirmed unpadded KitCard body and parent browser inspection identified text touching the edge. Parent recheck after publish.

- Installed package inventory, package remote discovery and remote/community management now retain persistent inline failures with refresh actions. Failed reads no longer become misleading empty success states. Focused bUnit regressions cover retry recovery; parent coordinates .NET execution.
- Builder graph has keyboard-accessible coordinate staging, explicit save-review separation, native finding buttons, selected-state semantics and responsive inspector controls. Four Node tests passed; offline Chromium proof passed desktop and 390px-wide frames with real Enter-key staging, independent frame channels and existing navigation-security assertions. This is synthetic intercepted topology, not live installed-package verification.
- Builder graph version 0.3.2, asset checksums and catalogue version updated. Proof output uses workspace scratch instead of /tmp.
- Notification device failure/retry implemented after parent observed failing regression. Unknown device inventory no longer reports notifications off or no devices. Final coordinated bUnit run passed. Remote/community management regression also covers independent retries and stops indefinite community skeletons.

## Source findings intentionally not implemented

- Current main already supplies package catalogue grouping, filters, search and graph view. Earlier checkout observation about absent filtering was withdrawn.
- SchemaFormRenderer still uses viewport-md column breakpoints. Needs a real narrow-widget reproduction before changing shared schema layout.
- Builder graph retains its own dark palette; no supported host-theme message contract was established, so no invented theme bridge was added.
- Layout editor already offers non-drag controls; it must not be described as pointer-only.

## Route inventory

Every route below was enumerated from current source. Focused reads covered package browse/installed/remotes/author, dynamic config, layout editor, settings overview/notifications and package schema renderer. Other admin routes require parent visual sweep; enumeration alone is not review.

| Route | Source |
| --- | --- |
| `/admin/activity` | `SharpMUSH.Client/Pages/Admin/Activity.razor` |
| `/admin/accounts` | `SharpMUSH.Client/Pages/Admin/AdminAccounts.razor` |
| `/admin/characters` | `SharpMUSH.Client/Pages/Admin/AdminCharacters.razor` |
| `/admin/discord` | `SharpMUSH.Client/Pages/Admin/AdminDiscord.razor` |
| `/admin/guests` | `SharpMUSH.Client/Pages/Admin/AdminGuests.razor` |
| `/admin/media` | `SharpMUSH.Client/Pages/Admin/AdminMedia.razor` |
| `/admin/profiles` | `SharpMUSH.Client/Pages/Admin/AdminProfiles.razor` |
| `/admin/backups` | `SharpMUSH.Client/Pages/Admin/AdminRemoteBackups.razor` |
| `/admin/server` | `SharpMUSH.Client/Pages/Admin/AdminServer.razor` |
| `/admin/wiki` | `SharpMUSH.Client/Pages/Admin/AdminWiki.razor` |
| `/admin/applications` | `SharpMUSH.Client/Pages/Admin/Applications/AdminApplications.razor` |
| `/admin/config/asciitranslations` | `SharpMUSH.Client/Pages/Admin/AsciiTranslations.razor` |
| `/admin/audit` | `SharpMUSH.Client/Pages/Admin/AuditLog.razor` |
| `/admin/config/bannednames` | `SharpMUSH.Client/Pages/Admin/BannedNames.razor` |
| `/admin/config` | `SharpMUSH.Client/Pages/Admin/Config/ConfigIndex.razor` |
| `/admin/config/{category}` | `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor` |
| `/admin/config/mssp` | `SharpMUSH.Client/Pages/Admin/Config/MsspConfig.razor` |
| `/admin/config/signin/{Provider}` | `SharpMUSH.Client/Pages/Admin/Config/SignInProvider.razor` |
| `/admin` | `SharpMUSH.Client/Pages/Admin/Dashboard.razor` |
| `/admin/messages` | `SharpMUSH.Client/Pages/Admin/GameMessages.razor` |
| `/admin/config/import` | `SharpMUSH.Client/Pages/Admin/ImportConfig.razor` |
| `/admin/import` | `SharpMUSH.Client/Pages/Admin/ImportDatabase.razor` |
| `/admin/jobs` | `SharpMUSH.Client/Pages/Admin/Jobs/AdminJobs.razor` |
| `/admin/kit` | `SharpMUSH.Client/Pages/Admin/KitPreview.razor` |
| `/admin/layout` | `SharpMUSH.Client/Pages/Admin/Layout/AdminLayouts.razor` |
| `/admin/layout/{Scope}` | `SharpMUSH.Client/Pages/Admin/Layout/LayoutEditor.razor` |
| `/admin/moderation` | `SharpMUSH.Client/Pages/Admin/Moderation.razor` |
| `/admin/navigation` | `SharpMUSH.Client/Pages/Admin/Navigation/AdminNavigation.razor` |
| `/admin/config/apps` | `SharpMUSH.Client/Pages/Admin/OAuthApps.razor` |
| `/admin/onboarding` | `SharpMUSH.Client/Pages/Admin/Onboarding.razor` |
| `/admin/packages/author` | `SharpMUSH.Client/Pages/Admin/Packages/AdminPackageAuthor.razor` |
| `/admin/packages/browse` | `SharpMUSH.Client/Pages/Admin/Packages/AdminPackageBrowse.razor` |
| `/admin/packages/remotes` | `SharpMUSH.Client/Pages/Admin/Packages/AdminPackageRemotes.razor` |
| `/admin/packages/review` | `SharpMUSH.Client/Pages/Admin/Packages/AdminPackageReview.razor` |
| `/admin/packages` | `SharpMUSH.Client/Pages/Admin/Packages/AdminPackages.razor` |
| `/admin/packages/batch` | `SharpMUSH.Client/Pages/Admin/Packages/PackageBatchReview.razor` |
| `/admin/characters/{Id:int}` | `SharpMUSH.Client/Pages/Admin/PlayerDetail.razor` |
| `/admin/diagnostics` | `SharpMUSH.Client/Pages/Admin/QueueDiagnostics.razor` |
| `/admin/config/restrictions` | `SharpMUSH.Client/Pages/Admin/Restrictions.razor` |
| `/admin/roles` | `SharpMUSH.Client/Pages/Admin/Roles/AdminRoles.razor` |
| `/admin/scenes/types` | `SharpMUSH.Client/Pages/Admin/Scenes/AdminPoseTypes.razor` |
| `/admin/config/secrets` | `SharpMUSH.Client/Pages/Admin/Secrets.razor` |
| `/admin/config/sitelock` | `SharpMUSH.Client/Pages/Admin/Sitelock.razor` |
| `/admin/config/sitelockrules` | `SharpMUSH.Client/Pages/Admin/Sitelock.razor` |
| `/admin/snapshots` | `SharpMUSH.Client/Pages/Admin/Snapshots/AdminSnapshots.razor` |
| `/admin/suggestions` | `SharpMUSH.Client/Pages/Admin/SuggestionManagement.razor` |
| `/admin/themes` | `SharpMUSH.Client/Pages/Admin/Themes/AdminThemes.razor` |
| `/settings` | `SharpMUSH.Client/Pages/Settings.razor` |
| `/settings/characters` | `SharpMUSH.Client/Pages/SettingsCharactersRedirect.razor` |
| `/settings/notifications` | `SharpMUSH.Client/Pages/SettingsNotifications.razor` |
| `/settings/theme` | `SharpMUSH.Client/Pages/SettingsTheme.razor` |

## Installed-package UI matrix

Source-defined examples are not assumed installed in the preview. Exercise each package with dependencies and permissions present, then absent/denied/empty states.

| Surface | Package | Runtime requirement |
| --- | --- | --- |
| /apps/chargen | chargen-app | Character application draft, validation and multi-step form |
| /apps/jobs | jobs-app | Empty queue, populated queue and permitted actions |
| /apps/boards | bboards-app | Board/thread/list/editor widths |
| /apps/highlights | highlights-app | Empty/populated highlights and actions |
| /apps/notifications | notifications-app | Character notification preferences, validation/save |
| /apps/duties | duties-app | Duty list and character actions |
| /apps/hello-ui | hello-ui plugin | Managed plugin loaded and schema/data endpoints available |
| Profile widget | relationships-app | Narrow profile placement, loading/empty/data |
| Name-card widget | relationships-card | Card scope, viewer-specific relationship lines |
| Hosted UI instance | builder-graph | Done offline; live installed instance not exercised |

Other shipped packages are primarily terminal softcode and integration surfaces. Catalogue inventory includes scene systems, +help/+config, wiki reader, profiles, jobs, radio, Discord, chargen, notifications, relationships, roster, time, health, availability, watch, bulletin boards, summaries, GMCP/Mudlet and softcode editor. Their presence in a catalogue is not proof of a styled terminal rendering. Terminal output belongs in parent play/terminal coverage.

## Visual evidence

- `images/visual-refinement-1-0/builder-host-proof.png`: desktop, inspected; readable coordinate inspector and staged-state copy.
- `images/visual-refinement-1-0/builder-host-mobile-inspector.png`: inspector scrolled into view, inspected; full coordinate inputs, visible keyboard focus, stage action and hint fit.
- Proof fixture intentionally has two fixed-height independent iframes; screenshots showing clipped lower graph content represent scrollable fixture boundaries, not full app screenshot acceptance.

## Remaining validation

- Parent coordinated bUnit tests: AdminPackageBrowseTests, AdminPackagesPageTests, SettingsNotificationsTests; broad parent suite after final edits.
- Parent real portal sweep: admin/settings/routes above at populated, empty, failed and loading states; long names/URLs, zoom, keyboard, dark/light themes.
- Independent proof: `node --test --test-isolation=none tools/client-tests/builder-graph.test.mjs` (4 passed).
- Native proof: `PLAYWRIGHT_MODULE=.../tools/responsive-sweep/node_modules/playwright-core node tools/hosted-ui/browser-proof.mjs` with TMPDIR/TMP/TEMP set to disk-backed workspace scratch (passed).
