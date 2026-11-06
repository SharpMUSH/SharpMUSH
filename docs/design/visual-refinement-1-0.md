# SharpMUSH 1.0 visual refinement

Baseline: `cfa5af9013`, reviewed on 2026-11-06 in parallel player, administration/package, and shared-accessibility workstreams. The existing visual identity, theme system and game-owned layouts remain the foundation.

## Findings and changes

| Finding | Refinement |
| --- | --- |
| Shared headers crowded controls and clipped long text | Wrapping card controls, scalable headings, contained long words, readable statistics labels |
| Failed reads looked empty or stayed loading | Persistent failure/retry states in character/help, wiki activity, package inventory/remotes/community and notification devices; plugin inventory remains independently reachable |
| Initial and hosted loading offered weak feedback | Accessible boot/application/dialog status, hosted bootstrap skeleton, keyboard-operable themed boot-error dismissal |
| Preview responsiveness followed the outer desktop instead of the selected size | Builder preview owns its layout container; phone/widget previews use one-column schema layouts |
| Keyboard selection did not consistently move focus | Builder settings focus handling; full radio keyboard behavior for appearance and preview-width groups; graph coordinate staging without pointer dragging |
| Mobile navigation obscured terminal connection controls | Terminal drawer and content reserve navigation clearance; pointer disconnect/reconnect verified in the real portal |
| Enlarged mobile navigation text was vertically clipped | Navigation height follows rem-based clearance and safe-area inset; text-range regression verifies visible labels |
| Custom author colors were unreadable in light themes | Hue-preserving adjustment against actual OOC, prose and bubble backgrounds; OOC blue improved from 2.029:1 to 4.504:1 on tested light themes |
| Filled semantic chips inherited white text on bright fills | Contrast foregrounds derive from each actual palette fill; Phosphor terminal improved from 1.424:1 to 14.745:1 in the actual browser |
| Mail drafts disappeared on navigation/reload | Tab-scoped drafts with account/character/compose isolation, explicit discard, send cleanup and storage-failure feedback; delayed sends preserve newer drafts |
| Wiki image metadata and account recovery content lacked readable presentation | Image-safe metadata foregrounds and consistent recovery-card padding |
| Native editor controls overlapped and save feedback inherited its dark canvas | Wrapping toolbar, shrinkable breadcrumbs and an opaque portal-themed status surface |
| Mermaid dark connectors faded against light portal surfaces | Fixed dark diagram canvas paired with its SVG palette and edge labels, preserving readability across theme changes |

The graph package is version 0.3.2 with updated asset checksums. Its existing iframe sandbox and host review boundary remain intact.

## Runtime and acceptance evidence

The continuation uses a fresh, isolated Production-auth world with real account sessions, dedicated NATS/JetStream, socket gateway and rendering worker. Ports bind to loopback. All characters/content are synthetic. No production data or services were changed. The published client endpoints manifest is installed beside the server, matching the Docker image contract; without it, logical lazy-asset aliases return 404 even when fingerprinted assets exist. The original editor loader required no product change.

- Real browser sign-in, gateway connection, `think Visual terminal journey verified`, and rendered response succeeded.
- A scene pose submitted through the real composer appeared in a second browser through `/hubs/scene`: one matching frame, zero observer navigation or scene/pose refetch, sender draft cleared.
- Real mail entry, saved status, browser reload, restored content, discard and populated message reading succeeded.
- Final published native editor saved and reloaded `add(6,7)`; toolbar overlap/clipping counts were zero, save feedback was readable and mobile panes did not overflow. No loader or CSS overrides were used. Mermaid rendered actual SVG at both widths; final dark canvas was visually accepted.
- Forty populated player captures cover ten routes at 1280 and 390 pixels in Daylight and Phosphor: profile/gallery, wiki article/editor/history/diff, mail/list/compose and scene/archive/live. No document overflow or page exceptions were observed. Representative screenshots received direct visual inspection.
- Seven-theme audit covers 21 desktop, 320px and 200%-root-text scenarios. Final initial sweep: no page overflow, no measured solid-background contrast failures, no active animations under reduced motion; ArrowRight moved appearance selection. Complex image/scrim backgrounds are explicitly excluded from automated contrast calculations.
- Initial broad route sweep measured 126 of 130 attempted captures without horizontal overflow; four authentication redirects were excluded and sign-in/registration were reviewed separately. This broad sweep used development auth and mostly first-viewport states; it is not equivalent to the populated real-auth journeys above.

## Major surface coverage

| Family | Coverage | Boundary |
| --- | --- | --- |
| Shell, navigation, Kit, themes | Real responsive views, keyboard, text enlargement, loading/error controls, seven themes | No blanket WCAG conformance claim |
| Authentication/onboarding/account | Sign-in/register visuals, real setup/sign-in/character handoff, settings/admin source and route review | No identity-provider/passkey/device end-to-end claim |
| Home and widgets | All 17 widget components inventoried; populated home/profile/wiki/scene compositions inspected | Not every possible game-owned layout combination |
| Wiki/help | Populated article, table/list, editor, three revisions/diff, help search/retry | Not every rich-media/translation combination |
| Characters/gallery/mail | Ordinary and admin profiles, uploaded gallery, relationships, message reading and draft recovery | Drafts remain local to the tab, not server-synced |
| Scenes/play | Populated archive/live composer, two-browser delivery, terminal command round trip | Not a long-running load/connection soak |
| Administration/settings | Inventory of configuration, people, moderation, content, portal and package pages; populated accounts/packages and error-state tests | Not every destructive/admin workflow executed |
| Builders/schema/maps | Source inventory, isolated preview reflow, actual three-room map selection/keyboard staging, schema package forms | Native/hosted graph world changes were staged, not applied |
| Softcode editor | Object/attribute tree, native editor language/worker, actual save/reload; final toolbar and status surface visually accepted at desktop/mobile widths | No exhaustive language-feature validation |

Exact component and route inventory: [historical first-pass ledger](visual-refinement-1-0-inventory.md).

## Installed package coverage

All bundled dependencies were installed through supported APIs. The first-party hello-ui plugin was locally built, hash-verified, installed and loaded by restarting only the isolated server. Hosted graph required the explicit browser-code opt-in in that world; omission from the catalogue before opt-in was expected policy. God is protected from package attribute writes, so player workflows use ordinary QACompanion/QAObserver fixtures.

| Surface | Populated evidence | Limit |
| --- | --- | --- |
| chargen-app | Saved concept/archetype/background/personality, desktop/mobile | Draft, not staff decision workflow |
| jobs-app | Real request and staff conversation/actions, desktop/mobile | Claim/close workflow not exercised |
| bboards-app | Root/nested boards and long-title post, desktop/mobile | Reply action not exercised |
| highlights-app | Real secondary-character pose and favourite, desktop/mobile | Take-back button not exercised |
| notifications-app | Ordinary-character kinds/preferences, desktop/mobile | Preference save/reload verified; OS push not requested |
| duties-app | Draft narration/end graph, editor and provider warning, desktop/mobile | Active party/roll requires a configured sheet provider; not accepted |
| relationships-app/card | Confirmed reciprocal Colleague profiles and actual scene name-card | Touch users have profile destination; no mobile hover claim |
| builder-graph | Real sandbox bootstrap, 3 rooms/2 exits, keyboard staging and host consent at both widths | No world mutation applied |
| hello-ui | Real plugin controller-driven fields/table at both widths | Read-only example |
| world-map/native editor | Actual 3-room graph and ArrowRight staging at both widths | Review/apply not exercised |

Terminal-only packages are represented by their actual help/profile/scene/room/job output and the component inventory; this report does not claim every softcode command was visually exercised. Server, database, messaging and renderer projects have no standalone web UI; their user-facing contracts were exercised through the real journeys.

## Verification

Final integrated component run: **3,473 passed**, zero failures. Final editor-focused follow-up: **31 passed**. Client JavaScript: **145 passed**; rendered shared-shell regressions: **6 passed**; actual Mermaid palette regressions: **2 passed**, with sampled node/edge/title text at 10.171:1. Graph keyboard behavior and the live host consent path were also checked. Builds use the pinned .NET 11 RC2 SDK and disk-backed workspace scratch.

Commands: `dotnet run --project SharpMUSH.Tests.BUnit`; direct execution of every `tools/client-tests/*.test.mjs`; `node --test tools/responsive-sweep/shared-shell.test.mjs tools/responsive-sweep/mermaid-contrast.test.mjs`; client publish; `git diff --check`. New behavior received independent code review, including fixes for stale-send cleanup and actual story-background contrast.

These are local functional/visual and laboratory checks. No field LCP/INP/CLS result, exhaustive assistive-technology certification, every-package/every-theme cross product, or provider-backed duty run is claimed. The published audit client is Debug; production payload performance is not inferred from its load time.

## Reproducing the published portal host

Use the pinned SDK and a dedicated disk-backed `work/` directory with `TMPDIR`, `TMP`, and `TEMP` set there. Publish both projects and retain the client endpoint manifest:

```sh
dotnet publish SharpMUSH.Server -c Debug -o work/visual-audit/server
dotnet publish SharpMUSH.Client -c Debug -o work/visual-audit/client
cp work/visual-audit/client/SharpMUSH.Client.staticwebassets.endpoints.json work/visual-audit/server/
```

Launch the server in Production with that server content root, the client `wwwroot` as web root,
a fresh `SHARPMUSH_LIGHTNING_PATH`, and a dedicated NATS instance with JetStream. Start the matching
SocketServer and RenderingWorker with a shared Unix socket; expose test ports only on loopback.
This audit used API18881, WebSocket4202, telnet4201 and NATS14225. Claim setup through `/api/setup`,
use real account sessions, and install first-party fixtures through package plan/apply. After any
client republish, refresh the manifest and restart the server so its endpoint mappings match.
Do not derive player-package acceptance from God: its protected attributes cannot be written by
wizard package executors. Use ordinary linked characters for applications, preferences and relationships.

## Selected screenshots

- [Recovered mobile mail draft](images/visual-refinement-1-0/mail-recovered-mobile.png)
- [Mobile terminal controls above navigation](images/visual-refinement-1-0/terminal-clearance-mobile.png)
- [Populated mobile character application](images/visual-refinement-1-0/chargen-populated-mobile.png)
- [Actual relationship card](images/visual-refinement-1-0/relationship-card-desktop.png)
- [Populated highlights](images/visual-refinement-1-0/highlights-populated-mobile.png)
- [Hosted graph consent](images/visual-refinement-1-0/graph-consent-mobile.png)
- [Native world map](images/visual-refinement-1-0/map-populated-desktop.png)
- [Compiled plugin UI](images/visual-refinement-1-0/hello-ui-desktop.png)
- [Native editor save feedback](images/visual-refinement-1-0/softcode-saved-desktop.png)
- [Mobile editor](images/visual-refinement-1-0/softcode-mobile.png)
- [Mobile Mermaid diagram](images/visual-refinement-1-0/mermaid-mobile.png)

## Research basis

- [WCAG 2.2 reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html): ordinary content reflows at 320 CSS pixels; intrinsic two-dimensional content may retain scoped scrolling.
- [Contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), [focus not obscured](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html), [target size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), and [status messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html).
- [WAI radio keyboard pattern](https://www.w3.org/WAI/ARIA/apg/patterns/radio/).
- [Web Vitals](https://web.dev/articles/vitals): field p75 targets LCP ≤2.5s, INP ≤200ms and CLS ≤0.1. Field measurements are outside this evidence.

The Goal remains active through final acceptance, committed changes, draft PR and CI/review follow-through. Merge and deployment are not authorized.
