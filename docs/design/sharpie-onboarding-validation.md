# Sharpie onboarding validation

Status: implementation in progress; not accepted for merge or deployment.

[Review the browser screenshots](sharpie-onboarding-preview/README.md).

## Current acceptance snapshot

The dated entries below retain the evidence as it was collected. Later entries
supersede earlier statements about missing publication or completed checks.

- Full component suite: **2,997 passed**, zero failed/skipped after integrating
  current main (`main-merge-bunit.log`).
- Desktop dark/light journeys cover local practice, explicit terminal sending,
  guide dismissal/focus return, and verified installed-package help commands.
- Browser viewports at 390px and 320px showed no horizontal overflow; keyboard
  local practice and independent entry into Play were exercised. This is browser
  emulation, not physical-device or virtual-keyboard acceptance.
- Artwork preferences, no-art DOM absence, reload persistence, reduced mascot
  sizing and the welcome reduced-motion style were checked in the browser.
- Administrator settings preview/save, verified/deferred checklist persistence
  and ordinary-player denial were exercised against the isolated server.
- Whole-branch source review completed; its remaining entry-link localization
  finding is addressed and the fresh full component suite passes.
- At `e524550fe`, GitHub build, formatting, core/component/integration/plugin
  tests, help integrity, parity, table layout and CodeQL passed. Automated
  Claude review failed before findings; independent review remains the
  substantive review evidence.
- Existing-character claim returned to the retained guide step and preserved
  the existing roster and active character in the real browser.
- Real failed-connection retry preserved the saved character, then connected
  successfully; the roster contained exactly one character afterward.
- Published administrator draft feedback passed the keyboard edit/reload check.
- Still open: broader keyboard/screen-reader checks and PR/CI verification.
  The fresh genre-art inventory is not approved.

## Requirement-to-evidence audit (2026-10-20)

This checklist separates demonstrated behavior from remaining acceptance work.
The dated records below provide the details and limitations of each observation.

| Requirement | Current evidence | Remaining gate |
|---|---|---|
| Four familiarity paths | Browser-selected all four paths; complete authenticated desktop Discord, fresh desktop MUSH and fresh mobile other-roleplay sequences through connected Play; returning mobile MUSH sequence | Remaining viewport coverage |
| Optional, dismissible, resumable | Independent route/reload, voluntary re-entry, Escape/back focus return, stop and resume tests; fresh keyboard login/create/welcome/Play journey | Screen-reader review remains open |
| Account and character handoff | Live create, existing-character claim, roster preservation, failed-connection retry; routing regressions | Fresh-account claim browser variant; configured external-login provider journey |
| Account isolation and anonymous import | Real HTTP checks, future-schema tests, live logout/second-account sequence: explicit import declined, fresh progress and RetryPilot context confirmed | No outstanding account-boundary case identified |
| Safe practice and real commands | Live local practice, empty-input insertion, draft refusal, explicit Send and core-command outputs; three live connections verify room speech, targeted pages and member-only cross-room channel delivery | No outstanding command-audience case identified |
| Installed-package accuracy | Reviewed-source/version/baseline/access regressions; live help evidence; open guide removes disabled `+help` and restores it automatically without losing focus | Actual uninstall scenario not exercised; disabled-handler transition verified |
| Game identity and feature gates | Public identity API checks, disabled/private wiki regressions, configured preview; all three configured wiki links opened as an ordinary player after admin changes, with disabled-reader omission verified | No outstanding configured-link case identified |
| Separate admin flow | `config.admin` denial/allow checks, preview/save, checklist persistence and deferred tasks | Full fresh-world First Setup and imported-world interrupted resume in browser |
| Layout and accessibility | Desktop light/dark, 320/390px reflow, named icon controls, focus trap/return, no-art/reduced imagery, dark paragraph contrast | Actual 200% zoom; broader screen-reader and theme contrast checks |
| Production art safety | Bounded approved neutral SVG fallback; candidates withheld after independent QA | All 252 inventory slots, independent QA and owner visual acceptance; approved genre assets wired into catalog |
| Documentation and review | Player/admin guides, design, validation records, packaged browser screenshots and draft PR #2069; existing review threads resolved | Final current-head CI and substantive review |

The failed automated Claude review produced no findings (one turn, no model
usage); it is not a successful review. CodeRabbit skips this draft PR. Neither
status closes the independent-review or final CI gate.

At implementation revision `81f983803`, GitHub build, formatting and component
tests passed; core and integration tests were still running when checked.
Claude review run `39386132471` failed after initialization with
`is_error: true`, one turn, zero cost and empty model usage. It produced no
buffered comments. The log does not establish the underlying service or
configuration cause, and this failure is not evidence of a code finding.

## Verified on 2026-10-19

- Full Blazor component suite: **2,873 passed, zero failed/skipped**.
- Targeted onboarding persistence/controller/catalog tests: **20 passed**.
- Disabled-wiki link regression: reproduced with a published page visible in
  onboarding while the reader was disabled; corrected and verified.
- Explicit accept/decline before importing anonymous progress, and preservation
  of future-schema local data, covered in the component suite.
- Isolated production-mode host with a fresh Lightning world and dedicated NATS:
  **12 real HTTP checks passed**. Disposable administrator/player accounts proved
  authorization, account isolation, optimistic-concurrency rejection and public
  identity/genre projection after an administrator save.

## Historical validation record

The sections below describe each check at the time it ran. The current acceptance
snapshot above is authoritative for remaining work.

The earlier browser failure is resolved. The local broker lost its port forward
after host thread exhaustion; the client also needed its separate publish output.
After restoring both, `/welcome` returned 200 and the in-app browser rendered the
Discord path and successful local `look` practice. Screenshot:
`work/onboarding/discord-practice-desktop.png`. This is one desktop dark-mode
interaction, not acceptance of all paths or keyboard/mobile behavior.

Additional checks: approved neutral mascot catalog 4/4 passed; all 113 direct
JavaScript tests across ten files passed. The client publish succeeded.

Independent review fixes now verified in component/catalog tests: four-step veteran
tour with separate completion, forum/tabletop explanations, authority-change
recovery, and an unsaved administrator identity preview. Five catalog tests pass.
Focus return, contextual/capability guidance and localization remain open, along
with the full runtime acceptance matrix.
Raw local logs and the credential-free HTTP status report are under
`work/onboarding/`; temporary test credentials are excluded from deliverables.

### Context and localization follow-up

All 2,880 component cases pass after localizing every player/admin lesson and moving contextual links into the lesson card. A selected-localizer regression and default-resource equivalence check cover all lesson paths. The isolated Production-hosted client was republished; browser inspection confirmed four MUSH steps, the distinct veteran lesson, and an enabled Scenes link inside that lesson. Screenshot: `work/onboarding/mush-context-refined.png` (dark desktop, preview mode). This is not proof of authenticated play, mobile layout, package command availability, or complete accessibility acceptance; those gates remain open.

### Package command discovery backend

The capability endpoint now supports reviewed plus-help1.8.0 and scene1.15.0 help commands, with installed version, pinned source reference, unchanged handler baseline and character-specific dispatch discovery. All27 onboarding server regressions pass, including unsupported versions, edited/shadowed/unreachable/mixed handlers, unsafe source paths and stale baselines. A separate real-engine regression passes: discovery returns a reachable handler without executing its body, then returns none after its use lock denies that character. Client presentation, installed-package end-to-end command results, and further adapters remain unverified.

### Signed-in runtime and connection recovery (2026-10-19)

- Restarted isolated loopback server with current capability backend (runtime session 46145). Signed in as the existing local test player and created JourneyTester without a native password. Creation succeeded but unavailable WebSocket gateway caused an unhandled CharacterCreate exception.
- Added retained-character connection retry with localized error and busy/finally guard. Handles false OTT result and network/cancellation failures without offering duplicate creation. Two regression cases reproduced failures before fix; CharacterCreate suite 6/6 and full component suite 2888/2888 pass (`work/onboarding/character-recovery-*.log`). Browser verification of repaired retry remains open; published client still predates this repair.
- Live signed-in guide correctly offered explicit anonymous-progress import/decline. Declined import, navigated to final lesson, and explicitly checked package suggestions for JourneyTester. Real backend returned the honest empty state; no positive installed-package claim. Screenshot `work/onboarding/signed-in-package-check.png`.
- Package UI uses captured session/character identity, cancellation and late-response guards, explicit GET only, and distinct empty/error states. Six package UI tests included in full suite.
- Remaining: publish repaired client and exercise recovery, run isolated socket/rendering services for positive connection/command journey, mobile/light/keyboard checks, remaining localization, installed-package walkthrough, logical commits/PR/CI.

### Sharpie visibility correction (2026-10-19)

User requested mascot presence throughout onboarding. GuidePanel now renders a compact approved mascot beside With Sharpie, so it remains present after the welcome header leaves view and in GuideCompanion during play. Welcome and companion pass the selected artwork and imagery preference. Unsupported/unapproved style inventory falls back to the approved neutral minimalist SVG; no-art still returns null, reduced uses 40px, standard 88px/64px responsive. No candidate art promoted. Added three component/catalog regressions; initial two failed as expected, full suite now 2891/2891 (`mascot-green.log`). Portal container breakpoint convention corrected to 48rem. Updated design and server catalog expectation. Published refreshed client successfully; real signed-in desktop screenshot `work/onboarding/sharpie-practice-visible.png` proves visible mascot in local practice card. Mobile and in-play visual acceptance remain open. Connection-recovery fix is now included in published client, but retry browser exercise remains open.

### Live terminal onboarding (2026-10-19)

Dedicated test gateway and renderer now run in containers. Gateway only publishes 127.0.0.1:4202; dedicated NATS publishes127.0.0.1:14223, server uses the existing isolated world on18080. Real browser OTT login connected JourneyTester. Found guide insertion only supported Story (unavailable without a scene). Added GlobalTerminal.TryInsertGuideCommandAsync: literal look only, interactive/connected/empty draft and identity rechecks; DOM check refuses composition/unreported draft. Reuses terminal focus, never sends. GuideCompanion's message no longer directs scene-less players to Story. Full bUnit2892/2892 pass; direct node layout35/35 pass (node --test only reported file wrappers, so direct result is the meaningful count). Required format passes complete.

Published and verified live: local practice stays separate; click inserts look into Game command with no new server output; explicit Send produces Room Zero output; Close guide removes companion and AX focus lands on Play container. Screenshot work/onboarding/sharpie-live-play.png shows mascot plus real room output. Mobile/light-mode/remaining command list and installed-package positive validation still open.

### Core-command walkthrough (2026-10-19)

Ran the remaining reference commands through the actual browser terminal on the isolated server: help say returned syntax and /noeval help; inventory returned empty inventory; who listed JourneyTester as the sole connected player; say echoed quoted speech; pose returned JourneyTester waves.; page to JourneyTester returned sender and recipient messages. AX evidence saved to work/onboarding/live-command-reference-evidence.txt. This single-character run verifies syntax and output, not multi-player audience isolation.

Added help say to GuideCatalog, distinct localized meaning, and rendered reference coverage. Corrected all novice/veteran/other-roleplay world and practice copy to acknowledge Story is available with a scene and ordinary Terminal supports first commands. Matching resource and docs updates pass full bUnit2892/2892 (verified-command-copy.log). The updated copy is built but not yet published to the running preview. Mobile/light testing remains unverified; CUA listed a CDP capability but no documented command interface was exposed in the current call, so no guessed API was used.

### UI localization completion pass (2026-10-19)

Moved Welcome familiarity choices, character routing text, skip state and account action; GuideCompanion controls/insert feedback; GuidePanel core-reference note; admin checklist labels/status/summary; GameIdentityForm fields, hints, preview, style labels and default SaveLabel; first-setup identity step into SharedResource. Existing English values preserved. GameIdentityForm accepts nullable SaveLabel to resolve the default through localization. Test fixture now registers localization as the real client does. Full bUnit2892/2892 pass (controls-localization.log and identity-localization.log), XML resource parse passes with zero duplicate keys, diffcheck clean. These changes are built but not published to preview yet. OnboardingService error strings remain to localize; do not claim full localization complete.

### Recovery review and light-mode acceptance (2026-10-19)

Localized the onboarding persistence errors. A failed guide reload now exposes a
collapsed recovery notice with Retry rather than hiding its only recovery control.
Character creation requires a successful initial roster read; a successfully
created character is retained when the subsequent roster read fails, and retry
verifies its DBRef and creation time without submitting creation again. An
additional character returns to the account or guide without changing the active
character. Independent follow-up review confirmed both fixes. Full bUnit2895/2895
passed; two additional routing cases brought the targeted CharacterCreate suite
to10/10. Required two-pass formatting and diff checks passed after the additional tests.

Published the tested recovery client (`publish-recovery.log`). Real browser checks:
other-roleplay selection and No artwork persisted after refresh; keyboard selected
No artwork and the page contained zero images; Reduced rendered a96px header mascot
and40px lesson mascot, both loaded. Evidence: `reduced-art-keyboard.png`. Existing
local test account login honored `/settings/theme` return routing. Selected Daylight
for the disposable JourneyTester character and verified the veteran welcome and
lesson in light mode: `veteran-light-desktop.png`, `veteran-light-lesson.png`. The
four-step veteran tour keeps its distinct introduction and optional command
reference. This does not establish mobile or comprehensive accessibility acceptance.

### Mobile and independent exploration (2026-10-19)

Used the supported browser viewport capability at390x844 and320x740. At390px,
expanded command help wrapped without horizontal overflow (document scrollWidth
and clientWidth both390). At320px, Discord local practice accepted `look` via
keyboard Tab/Enter, displayed the courtyard result and kept scrollWidth equal
to clientWidth320. Screenshots: `mobile-command-reference.png`,
`mobile-practice-320.png`. With reduced motion emulated, the welcome container
had animation-name none and transition-duration0s; this is a scoped style check,
not comprehensive screen-reader proof. The practice result has role=status.

Choosing independent exploration at320px navigated directly to Play with no
companion. OTT connected JourneyTester, and explicitly sending `look` returned
Room Zero output from the isolated server. Screenshot `mobile-independent-play.png`.
Reset both viewport and emulated media afterward. These are real browser journeys
at mobile viewport sizes, not physical-device or virtual-keyboard validation.

The positive package check exposed a bug: the server already installs plus-help
1.8.0 and scene1.15.0 from bundled:sharpmush, but the capability resolver only
recognizes pinned Git sources. A regression is being added; this gate remains open.

### Bundled package suggestions fixed and verified (2026-10-19)

The new bundled-source test reproduced the omission (12/13 passed, bundled
case failed). The resolver now recognizes only the reserved source and commit
marker with matching package-id path, while keeping reviewed-version, live
reachability and managed-baseline checks. Git sources retain their pinned URL
requirements. Bundled suggestions have a nullable reference and a localized
“Included with this server” label. Independent review found no concrete issue.
All13 server capability cases and the full2898 component cases pass. Published
client and restarted only the isolated loopback server.

Real browser evidence: explicit checks for JourneyTester return +help1.8.0 and
+scene/help1.15.0. Both commands were separately sent through the real terminal:
+help returned the installed topic sources; +scene/help returned scene command
syntax. Evidence: `package-help-live.txt`, `installed-package-suggestions.png`.
Screenshot review found source-label spacing, corrected with an explicit separator;
republished successfully and verified the explicit separators in the live accessibility tree. The full-page screenshot includes Sharpie and both suggestions.

### Administrator journey (2026-10-19)

The ordinary player was denied `/admin/onboarding`. Signed in with the disposable
administrator and reviewed prefilled settings. Changed introduction and selected
Comicbook style; the unsaved preview updated. Saving persisted the settings and
marked identity verified. Deferred joining, reloaded, and confirmed one verified
and one deferred item plus the saved introduction/style. The intentionally unsaved
later draft was discarded on refresh. Screenshot: `admin-checklist-persisted.png`.

Found and fixed stale Saved feedback after editing: targeted regression failed
before the fix and passed afterward; every bound identity field now clears that
feedback. Full component suite still passes 2,898 cases. The joining checklist link
now targets the actual player access controls (`/admin/config/Net`), verified in the
browser. Default wiki templates no longer advertise obsolete `create` login syntax;
they link to the optional guide and current account/character flow. Existing game
wiki content is not migrated or overwritten.

### Draft PR and further keyboard checks (2026-10-19)

Draft PR: https://github.com/SharpMUSH/SharpMUSH/pull/2069.
The published implementation head is 1d52c080a. Formatting CI passed; the build
was still running when checked. The automated Claude review failed immediately
after initialization with no findings and was retried once. This is not a review
pass; CodeRabbit explicitly skipped the draft.

In the actual browser, an expired disposable account session rejected a guide
choice with a recoverable save error instead of claiming success. Reload returned
to anonymous browser progress. Keyboard activation advanced the other-roleplay
path past its already-completed steps to the audience/consent lesson; Return
expanded the command reference. The accessibility tree exposed the new heading,
expanded state and command/audience definitions. The reduced Sharpie remained
visible. Screenshot: `work/onboarding/roleplay-keyboard-reference.png`.
This verifies these interactions, not a complete screen-reader assessment.

### Additional-character runtime journey (2026-10-19)

Signed in through the visible login form as the disposable player with Remember me
unchecked. Created JourneySecond using the existing no-chosen-password option.
The portal returned to /account, listing JourneyTester (#17) and JourneySecond
(#18). Opening Play authenticated JourneyTester through OTT and displayed Room
Zero; creating the additional character did not switch the active character.
The contextual Sharpie guide remained available and expanded successfully.
Screenshots: `additional-character-account.png` and
`additional-character-original-play.png` under `work/onboarding/`.
The claim and failed-connection retry journeys remain open.

### Play-height correction (2026-10-19)

User review rejected the guide stacked above Play. Replaced it with an explicit
header control opening the existing dismissible side-sheet surface. The terminal
remains mounted and no guide content occupies the Play flex column. Desktop
measured 918x836 before and during opening; at a 390x844 viewport Play remained
374x828, with document scroll width 390. Escape closed the sheet and restored
focus to the opener. Screenshot: `work/onboarding/play-guide-closed.png`.
A component regression checks that the dialog is outside the Play layout and
the game card remains mounted. This panel is modal while explicitly open; close
it to resume typing. Further refinement of guide insertion/focus is pending.


## Guide navigation and review follow-up — 2026-10-19

The Play guide now has one sheet boundary, with both inner card borders removed.
Back to game and Open the full guide or change my starting point are button-styled
controls together above the lesson. The latter remains a semantic navigation link.
There is one full-guide link in the sheet for all progress states. The outer
accordion is gone; the optional command reference retains its disclosure.
Back to game preserves the journey, while Stop this guide dismisses it.

Published client checked in the real browser: both top controls are present,
Sharpie remains visible, and the inner frames are absent. Evidence:
`work/onboarding/guide-navigation.png`. Viewport screenshot capture was used;
full-page capture resizes this responsive surface and closes the sheet.
Full bUnit suite: 2,901 passed, zero failed/skipped. Changed C# whitespace and
`git diff --check` pass. Successful insertion closing the panel is component-tested;
its latest real-browser acceptance remains open.

Ten automated review threads were answered with ownership/control-flow evidence:
six response-disposal warnings transfer ownership to consumers that dispose the
response; three state guards intentionally recheck after awaited JS calls; the
optional presentation boundary logs failures, propagates caller cancellation,
and returns no unverified links. No safety guards were removed to silence these
findings. CI and broader end-to-end acceptance remain required.


## Icon controls and live draft protection — 2026-10-19

Published client at `058830c83`: Back to game and full-guide navigation are
icon-only controls with localized accessible names/tooltips. Stop this guide is
also an icon, and the lesson header no longer displays “With Sharpie”. An inactive
journey now offers four starting points inside the Play sheet rather than leaving
it empty. Browser selection starts/resumes the lesson in place. The full component
suite passes 2,902 tests, including the new inactive-path regression.

Live account/terminal evidence on the isolated world: inserting `look` into an
empty connected input closed the sheet without sending and retained the current
lesson on reopening. A second insertion with that draft already present left
`look` unchanged, kept the sheet open, and explained the refusal. Back to game
preserved the draft; only explicit Send echoed `look`, cleared the input, and
produced the room response at 23:17:34. Screenshots:
`work/onboarding/guide-stop-icon.png` and
`work/onboarding/guide-draft-preserved.png`.

Focus currently returns to the guide opener after successful insertion; improving
that handoff to the command input remains an accessibility/usability follow-up.
Character claim, failed-connection retry, administrator draft-feedback runtime
checks and broader accessibility acceptance remain open. Genre lesson pilots
are under independent visual review and are not shipped or owner-approved.


## Channels and command focus — 2026-10-19

Published client includes `a3e7e1305` channel guidance and `422af8a37` focus fix.
The dedicated “Find people who can help” lesson in every guided path teaches Browse channels, `@channel/list`, reading
channel descriptions, joining, chatting and leaving, with explicit cross-room
audience and game-dependent names/permissions. Reference commands are localized.
The live ordinary-player terminal accepted `@channel/list` and returned the
correct empty-world result, “No channels.” Join/chat syntax was checked against
shipped channel help; no public message or membership change was made.

Real-browser insertion into the empty connected input closed the guide and left
focus on Game command with value `look`, without sending. This supersedes the
previous focus follow-up. Standard Back to game still returns to its opener.

The channel lesson's document scroll width equaled the viewport at 390px and
320px. At 390px its content width was 314px; command examples wrapped within the
card. Enter expanded and collapsed the common-command reference with focus on
its summary; channel entries displayed their localized meanings and audiences.
The viewport override was reset afterward. These are browser viewport/keyboard
checks, not physical-device or screen-reader acceptance. Screenshot:
`work/onboarding/channels-phone.png`.

### Dedicated channel step, automatic suggestions and existing-character claim

At implementation commit `1abd82725`, channels are a separate stable step before
the final lesson (eight basic steps, five veteran steps). Reloading `/welcome`
retained the selected channel step. Final-step package suggestions loaded
automatically for the active character, showing `+help` and `+scene/help` without
a discovery button. The full local bUnit run passed 2,903 tests with no failures
or skips (`work/onboarding/guide-auto-final.log`).

In the isolated loopback world, signing into the ordinary test account preserved
the nested return route to `/characters/claim?returnUrl=%2Fwelcome`. Claiming the
existing disposable character ClaimPilot returned to `/welcome` and the same
channel step. The account roster then showed ClaimPilot #19 alongside the
unchanged JourneyTester #17 and JourneySecond #18; JourneyTester remained active.
Screenshot: `work/onboarding/claim-roster.png`. No production account or world was
used.

Remote CI for that exact commit passed build, formatting, bUnit, scene and Discord
plugin suites, parity and security analyses at inspection. Core tests, integration
and help-integrity were still running; `claude-review` failed. This is not a claim
that all CI or review gates have passed.

The later response-disposal review finding on
`GuidePackageCommandsTests.cs:103` was checked against the consuming component.
The fake handler transfers its response through `HttpClient.SendAsync`;
`GuidePackageCommands.Check` owns it with `using var response`, including
failure/cancellation exits. Disposing inside the handler would prematurely
dispose the response before the consumer can read it. No production ownership
change is needed for this finding.

### Real first-character connection recovery

Stopped only `codex-sharpie-onboarding-gateway-network` in the isolated preview.
A fresh disposable account created RetryPilot with the passwordless option.
The UI retained the saved character, removed creation inputs and showed
“Retry connection” with an honest failure message. After restarting that same
gateway, retry reached Play connected as RetryPilot, with the game-command input
focused. Failure screenshot: `work/onboarding/connection-retry-failure.png`.
The gateway is restored. Subsequent browser inspection of the account roster
showed exactly one character, RetryPilot #20: retry did not duplicate creation.

This run also reproduced a separate return-route bug: signing in with no
characters from `/login?returnUrl=%2Fcharacters%2Fnew%3FreturnUrl%3D%252Fwelcome`
landed at `/characters/new` without the nested guide destination, and recovery
therefore went to Play. `Login.OnboardingUrl` currently preserves direct welcome
routes only. This needs a regression and safe preservation of the character
creation/claim destination before this journey can be accepted.

The routing regression reproduced both lost create and claim destinations
(2 failed, 9 passed). Login now preserves only the exact local character-entry
routes, with their query strings; CharacterCreate still independently restricts
its final destination to the local welcome route. Three negative cases cover
protocol-relative, near-match and traversal paths. Browser publication and
rechecking this fix remain pending.

### Published first-character guide return (2026-10-20)

Published commit `9d3e2f8fa` successfully to the isolated client preview.
A fresh character-free account signed in through the nested character-creation
return URL. The browser retained `/characters/new?returnUrl=%2Fwelcome`.
Passwordless creation of RoutePilot then connected to the test game and returned
to `/welcome`, with focus on the welcome heading. Existing anonymous guide
progress was offered for explicit import rather than silently copied.
Screenshot: `work/onboarding/first-character-guide-return.png`.
This closes the reproduced nested creation-return bug; claim routing also has
the passing regression, while its fresh-account browser variant remains untested.

### Administrator draft feedback (2026-10-20)

Signed into the isolated administrator and saved the existing introduction
unchanged. “Saved” appeared after the request completed. A native keyboard edit
and Tab out of the introduction field removed that status and updated the
unsaved preview. Reload restored the original introduction, proving the draft
was not persisted. The verified/deferred checklist values survived reload.
No registration, permissions, package installation or production settings changed.

### Immediate preview and path-choice focus corrections

The input-event regression failed before the form was changed to update on each
keystroke. Text fields now update the draft preview and clear saved feedback
without requiring blur; persistence still requires Save. The style is labelled
“Anime mascot.” A neutral-art notice explains why unshipped style/genre choices
currently show the approved default mascot.

The configuration and onboarding settings controllers both require `config.admin`.
Live isolated GET checks returned 403 for the ordinary player and 200 for the
administrator on both endpoints.

Keyboard selection in the inactive Play guide reproduced focus falling onto the
document after its path button disappeared. The companion now focuses the lesson
heading after rendering that transition, without adding the heading to tab order.
The regression failed before the fix. Full component validation then passed
2,908 tests, zero failures/skips (`admin-preview-focus-green.log`).

### MSSP choices and published keyboard follow-up (2026-10-20)

Genre and subgenre now use the shared `MsspCatalog` choices in single-select
controls. The onboarding settings endpoint requires exactly one listed value
for each; `None` is the explicit absent-category choice. Existing unsupported
values remain visible as disabled options until the administrator selects a
supported replacement. No unrelated MSSP variables are edited.

The full component suite passed 2,912 tests. The local server and client were
updated and the browser showed Fantasy / High Fantasy selected with the catalog
options available, plus the simplified Anime mascot label. Screenshot:
`work/onboarding/mssp-single-select.png`.

In the published Play guide, keyboard selection of the Discord path placed focus
on its lesson heading. Escape closed the sheet and restored focus to the guide
opener. The retained lesson resumed correctly. These verify DOM/native keyboard
focus behavior, not an actual screen-reader session.

The published dialog exposes `role=dialog`, `aria-modal=true` and a name.
All seven interactive controls inspected had accessible names. Navigation and
stop icons measured 44×44 CSS pixels; lesson action buttons were 44px high;
the reference disclosure measured 287×36. Shift+Tab from the first control
reached the focus-trap sentinel inside the dialog, and Tab returned to the first
control without entering the underlying game. This is a bounded focus-containment
check, not proof of every screen-reader or keyboard interaction.

At a 390×844 viewport, the published dedicated channel lesson had document
width 390px and dialog width/scroll-width 309px, with no horizontal overflow.
Its vertical scroll height was 1000px. Keyboard navigation reached the final
reference disclosure and scrolled it fully into view (top 787.8px, bottom
823.8px). Escape returned focus to the Play guide opener; reopening retained the
channel lesson. The viewport override was reset. Screenshot:
`work/onboarding/channel-step-mobile-final.png`. This remains viewport emulation,
not physical-device or virtual-keyboard testing.

### Channel lesson simplification

Following user review, the primary channel lesson now shows only discovery,
joining and asking a question. Each command has a separate, short explanation
in an unnumbered definition list with explicit zero list margins and spacing
between rows. The long help/safety paragraph and secondary description/leave
commands remain out of this introductory step; fuller instructions are available
in the player guide. Channel audience and the meaning of NAME remain explicit.


### Simplified channel lesson reflow (2026-10-20)

The published client at commit `3b08a23eb` was visually checked at desktop
and 390×844 viewport sizes. Three command/description rows now have clear
spacing and inset margins. Full component tests passed 2,912/2,912.
Screenshots: `work/onboarding/channels-simple-desktop.png` and
`work/onboarding/channels-simple-mobile.png`.

At 320×568 CSS pixels, document width remained 320px; the guide dialog
measured 264px client and scroll width, and the command list measured 232px
client and scroll width. Vertical scrolling remains necessary (821px content
height). Tab from the final reference disclosure wrapped to the Back to game
control inside the dialog. This verifies narrow reflow and that focus boundary,
not browser zoom, a screen-reader session, or physical mobile hardware.
The viewport override was restored after checking.


### Familiarity selection and independent return (2026-10-20)

In the published anonymous desktop client, switching from Discord to MUSH
showed the five-step veteran path and retained prior completion markers.
The inspected lessons covered web-account versus character-password identity,
claiming existing characters, Story/Terminal modes, channel help, and optional
game-specific additions. Previously completed steps remained accessible from
the step list; advancing a completed introductory step resumed the incomplete
channel lesson. The roleplay path showed its distinct persistent-world intro.

Choosing independent exploration navigated immediately to Play without opening
the guide. Reloading kept it closed. Explicitly opening Sharpie's control then
offered familiarity choices; selecting roleplay restored the selected intro and
focused its heading. Screenshot:
`work/onboarding/independent-return-roleplay.png`. This checks anonymous path
selection, persistence and voluntary re-entry, not a fresh authenticated
end-to-end journey for every path.


### Welcome keyboard and contrast checks (2026-10-20)

Keyboard Enter selected Discord; Tab then Enter selected MUSH and retained
focus on the selected choice. The four native button elements exposed their
full title/detail names and exactly one `aria-pressed=true`. At 320×568 the
document stayed 320px wide; all four choice cards had equal 276px client/scroll
widths and heights of 91–112px, without horizontal overflow. Screenshot:
`work/onboarding/welcome-keyboard-narrow.png`. Viewport restored afterward.

The dark Play dialog background was RGB(22,24,27). Main paragraph text
RGB(233,237,240) measured 15.11:1 contrast; the supporting NAME instruction
RGB(125,135,144) measured 4.86:1. These are the inspected dark-theme paragraphs,
not a contrast audit of all themes or controls. Browser zoom shortcuts did not
change viewport dimensions or device-pixel ratio in this browser; therefore
200% browser zoom remains unverified.


### Returning login, character switching and channels (2026-10-20)

The guide sign-in link preserved `/welcome?returnUrl=%2Fplay`. Signing in as the
existing ordinary test account restored its own saved progress (rather than the
anonymous profile's completion markers) and JourneyTester as active character.
The real account roster retained JourneyTester, JourneySecond and ClaimPilot.
Switching through the account menu to JourneySecond updated the guide's character
context; Play connected as JourneySecond and reopening the guide retained the
character lesson with that same identity. Screenshot:
`work/onboarding/character-switch-guide.png`.

In the isolated world, `@channel/list` first returned `No channels`. A disposable
OnboardingHelp player channel was created through the engine. Listing then showed
it; leaving and rejoining with `@channel/on OnboardingHelp` produced the expected
confirmation and sidebar update. `@channel/who` confirmed JourneySecond was its
only member before the guide's `@chat OnboardingHelp=Can someone help?` example
was explicitly sent. The engine echoed the message on that channel. This proves
the sequence and self delivery, not multi-player audience isolation. Evidence:
`work/onboarding/live-channel-sequence.txt`.

Story-mode validation reached the installed scene package's real participation
restriction: JourneySecond is not approved. No scene was created. Automatic
approval review rejected assigning `approved` to this disposable character
without explicit permission; that exact role/recipient/test-world request is
pending with the user. No permission was changed or alternate bypass attempted.

At head `bb46d34bb`, build, formatting, bUnit, scene/Discord plugin tests, help
integrity, layout, PennMUSH parity and code analyses passed. Core and integration
test jobs were still running at inspection. Claude review failed before producing
findings; CodeRabbit skipped the draft. These do not establish full CI acceptance.


### Story composer runtime proof (2026-10-20)

After explicit user approval, the local administrator assigned the `approved`
role to disposable JourneySecond (#18). The private Onboarding Story validation
scene was created through the normal scene UI. In connected Play/Story mode,
the guide inserted `look` into the empty composer, switched Say to Command,
closed the sheet and focused the composer. The scene still showed no activity.
Appending a test draft and trying insertion again kept the sheet open, displayed
the refusal explanation and preserved `look draft-preservation`. No Send action
was taken. Screenshot: `work/onboarding/story-draft-preservation.png`.

The completed core CI run at `bb46d34bb` found one failure: the veteran catalog
test still expected four steps. Its expected IDs now include the user-requested
`channels` lesson before `continue`; the no-command-practice veteran assertion
remains unchanged. Integration CI passed.


### Independent review: capability repaint (2026-10-20)

Review found that already-rendered package commands could remain visible during
a new account/character capability request. Two regressions first failed with
one stale command still rendered. The component now repaints immediately after
starting the check (which clears its previous state), then again after completion
if still alive. Both account and character transitions are covered with the new
response held pending. Full bUnit passed 2,914 tests; direct formatting and
independent focused source review passed. Commit: `8e6ea1aef`.

The connected Story practice guide was also checked at 390×844: document width
390px and dialog client/scroll widths both 309px. Its existing draft remained
present after the viewport change. This supplements desktop insertion/refusal
evidence; it does not verify a physical on-screen keyboard.

### Live account isolation (2026-10-20)

Signed out of onboarding-player through the account UI, then signed into the
separate existing retry fixture. The welcome page offered an explicit choice
to import anonymous browser progress; no familiarity path was selected.
Choosing “Keep my account progress” and Discord started with no completed
steps. The character lesson identified RetryPilot, not JourneySecond. This
verifies the visible account boundary and opt-in anonymous import in the live
browser. Screenshot: `work/onboarding/account-isolation.png`.

### Authenticated Discord lesson sequence (2026-10-20)

RetryPilot completed all eight lessons from the introduction through the final
lesson using Return on the advance control. Local `look` practice produced only
the practice-room explanation. The channels lesson showed three commands with
one-line explanations. The final lesson automatically displayed verified
`+help` and `+scene/help` suggestions. Completing the final step replaced the
lesson with the completion message. “Open the game” reached connected Play as
RetryPilot in Room Zero, with the command input focused and no guide overlay.
This proves the authenticated desktop lesson sequence; it does not substitute
for a fresh login/claim journey or mobile execution of every path.

### Returning veteran path on mobile (2026-10-20)

At 390×844, authenticated RetryPilot selected the MUSH path after completing
Discord. Shared completed topics remained marked. Each of the five veteran
lessons was opened explicitly and showed the expected distinct content:
MUSH foundations, web account/character linking, Story versus Terminal,
channels, and installed additions. No command-practice step was added.
Document scroll width equalled the 390px viewport. The Play-modes lesson was
visually inspected and its unchanged screenshot added to the review gallery.
Return on the final advance button displayed completion. This checks a returning
account's mobile review flow, not a new account's first-time progression.

### Final lesson focus correction (2026-10-20)

Independent review found that final Complete/Skip removed the focused button
without selecting a completion destination. Both regression cases failed before
the correction. Explicit completion now focuses the rendered completion message
once; passive reload and subsequent imagery changes do not trigger that focus.
All 2,916 component tests and direct formatting passed. Focused independent
review accepted the correction.

After publishing to the isolated preview, both Complete and Skip at 390px placed
`document.activeElement` on `.welcome-completion`. Tab then moved to “Open the
game.” Passive reload left focus on the document body, not the completion message.
Screenshot: `work/onboarding/completion-focus-mobile.png`.

CI run 39380118401 for implementation commit `8e6ea1aef` completed successfully:
build, format, core, integration, bUnit, both plugin suites and help integrity.
This precedes the completion-focus correction and does not establish CI for it.

### Icon vocabulary and keyboard tooltips (2026-10-20)

The full-guide icon is now a book and stopping guidance uses a stop-square;
returning to play retains its back arrow. Controls retain accessible names and
44px hit areas. Visual labels appear on keyboard focus or hover, remain reachable
inside the hover wrapper, and dismiss on Escape. No extra focus stop is added.
See [research and recommendations](sharpie-onboarding-visual-standards.md).

The first live check caught an Escape propagation bug not exposed by component
tests: Blazor invokes the handler before checking its propagation flag, so
synchronous dismissal removed that flag before reaching the parent sheet.
Deferring the visibility change until after current event traversal fixes it.
The repeated live check at 390px showed one dialog and no tooltip after the first
Escape, then zero dialogs and focus on the guide opener after the second.
Stop-tooltip placement was visually inspected and captured in
`work/onboarding/guide-tooltip-mobile.png`. All 2,917 component tests, formatting
and local publication passed. Touch icon comprehension remains a user-test item.

### Fresh other-roleplay mobile journey (2026-10-20)

A new disposable local account was used; no existing account progress was reset.
At 390×844, signing in with `/welcome` as the return destination led to first
character creation. Creating RoleplayPilot without a chosen native password
returned to `/welcome`. Declining anonymous progress left all eight steps fresh.
The roleplay path displayed its distinct persistent-world introduction,
forum-versus-room navigation explanation and collaborative dialogue guidance.

All eight lessons were traversed with Enter on the advance control. Local
`look` practice returned the practice room; installed `+help` and `+scene/help`
appeared automatically in the final lesson. Completion focused the completion
message, then Tab/Enter on “Open the game” reached connected Play as RoleplayPilot,
with no guide overlay and focus in the command input. Explicitly sending `look`
returned the actual room description. Screenshot:
`work/onboarding/fresh-roleplay-mobile-play.png`.

### Open-guide package freshness regression (2026-10-20)

Inspection found that package commands refreshed only on initial mounting or
account/character changes. A controlled-clock component regression reproduced
the stale command after package availability changed.

The component now rechecks every 30 seconds while mounted, without sending a
game command or adding a manual discovery button. Background checks preserve
the current display while pending; an empty response removes commands and a
failed verification clears them and shows the existing unavailable status.
Requests retain the 10-second timeout and authority/generation guards. Disposal
cancels the refresh loop. This bounds freshness rather than promising immediate
package-change notification. Real-browser package removal remains an open gate.
Both removal and failed-verification cases pass, including no further requests
after disposal. The full 2,919-case component suite and whitespace check pass.

### Live package-command availability transition (2026-10-20)

Published revision `2cab805af` was loaded in the 390px browser, connected as
RoleplayPilot. The open final veteran lesson initially listed `+help` and
`+scene/help`. A read-only administrator query identified Help Librarian as
`#10` and confirmed `hasflag(#10,NO_COMMAND)` was `0`.

Temporarily setting `NO_COMMAND` on that object through the normal command API
returned `1`; the still-open guide removed only `+help`. Clearing the flag
returned `0`, restoring the original state, and `+help` reappeared automatically.
Both observations retained one dialog and focus on “Explore this game's
additions”. No navigation, guide reopening or account change occurred. There
were no browser error logs at the final observation.

One locator wait expired before the next refresh; a subsequent accessibility
snapshot and DOM observation confirmed both restored commands. This proves the
transition, not an exact measured refresh latency. The fixture changed command
dispatch availability; it did not uninstall the package or edit its attributes.
Evidence: `package-refresh-disabled.json`, `package-refresh-restored.json`,
`package-refresh-disabled.png`, and `package-refresh-restored.png` under
`work/onboarding/`.

### Keyboard action audit and Explore return (2026-10-20)

On the final lesson at 390px, ten successive Tab presses traversed Scenes,
Explore, Back, Skip, Complete, the command reference, Back to game, full guide,
Stop and Scenes again. Every focused control remained inside the modal. All
seven icon controls measured 44×44 CSS pixels. Shift+Tab to Back to game and
Enter closed the modal and restored focus to its opener; Enter reopened it.

The same audit reproduced an ineffective Explore action: following its `/play`
link while already on `/play` left the dialog open and focus on that link.
The component regression failed before the fix. The Play host now supplies an
explicit Explore callback that closes the guide and focuses the existing
composer, preserving guide progress. Other hosts retain normal link navigation.
This is keyboard/DOM evidence, not a screen-reader or 200% zoom certification.

After publication, entering an unsent `look` draft, opening the guide and
activating Explore with Enter yielded zero dialogs, focus on “Game command”,
and the unchanged `look` draft. No command was sent. Screenshot:
`work/onboarding/explore-return-draft.png`. The full 2,920-case component suite,
formatting and publication passed.

### Review: asynchronous guards and response ownership (2026-10-20)

Two disposal findings in the capability tests were addressed by explicitly
scoping response fixtures and waiting for the deferred response to render before
disposing it. Two constant-condition findings were not applied: both checks
follow an `await`, during which Blazor can process a disconnect or dispose the
component. Comments now explain those reentrancy boundaries.

`DisconnectDuringGuideInputCheckRefusesInsertion` holds the JavaScript busy
check pending, renders the composer disconnected, then releases the result. It
confirms no draft insertion or send occurs. All 2,921 component tests and
formatting pass. Independent source review of periodic refresh, Explore return,
response guards and the current cleanup found no concrete actionable defect;
that review did not run tests.

### Fresh veteran keyboard journey (2026-10-20)

A new disposable account was registered without resetting existing progress.
At 1280×900, keyboard-only interaction from the sign-in page entered the existing
fixture credentials, disabled Remember me and submitted. First-character
creation retained `returnUrl=/welcome`. Tab/Enter on Skip to main content and
Tab reached Character name; typing VeteranPilot, selecting the no-chosen-password
checkbox with Space and submitting with Enter returned to `/welcome`.

Anonymous import was declined by keyboard. Selecting the MUSH path showed five
fresh lessons: MUSH differences, account/character separation, Story/Terminal,
channels, and game-specific additions. No practice panel appeared. Enter
completed each lesson; completion focused its message, then Tab/Enter opened
Play connected as VeteranPilot with focus in Game command. Explicitly typing
and sending `look` returned the room description. Screenshot:
`work/onboarding/fresh-veteran-keyboard-play.png`.

This run also found two accessibility gaps: path selection left focus on the
choice and required eight further Tab presses to reach Complete; create/claim
used an h5 for the page title. Regressions reproduced both. The fixes focus and
scroll the chosen lesson once after a successful selection and expose the
create/claim title as h1 while preserving its visual typography.

After publishing the fixes, keyboard activation of the MUSH choice in the live
preview focused the `Just what's new` h2 and scrolled it into view. The live
create page exposed `Create a character` as h1. The full component suite passed
2,925 tests, with zero failures or skips; targeted formatting and `git diff
--check` passed. Screenshot: `work/onboarding/welcome-path-focus.png`.

### Narrow-screen veteran focus and contrast (2026-10-20)

At 320×740 in the published player-guide preview, keyboard activation of the
MUSH choice focused the lesson h2 at viewport y=386px. Document width remained
320px. Stop, Back, Skip and Complete each measured 44×44px. Tab reached Skip
(Back was disabled on the first lesson); another Tab and Enter completed the
lesson and displayed the account/character lesson without horizontal overflow.
The action retained focus, and the changing heading has `aria-live="polite"`.
This verifies DOM semantics and keyboard behavior, not screen-reader speech.

Computed dark-theme heading, body and enabled action foreground was
RGB(233,237,240) on RGB(22,24,27), giving 15.11:1 contrast. This measurement
does not establish contrast for other themes, focus rings or disabled controls.
Screenshot: `work/onboarding/welcome-veteran-320-focus.png`.

### Main integration and renewed review (2026-10-20)

GitHub reported the branch conflicting with main. Integrated main at
`d578d34d5`; the only conflict was adjacent additions to SharedResource.resx.
Both onboarding resources and WikiStartHere were retained. XML parsing and
uniqueness checks passed for 3,453 resource keys. The integrated full component
suite passed 2,997 tests with zero failures or skips.

Four new disposal findings were traced to consumer-owned HTTP responses:
GuidePackageCommands and OnboardingService dispose their awaited responses with
`using var`. Ownership evidence was added to each review thread, and all four
were resolved. Current-head remote CI still requires verification after push.

### Configured guide links as an ordinary player (2026-10-20)

Created three clearly named public reference fixtures in the isolated world
and configured WelcomePage, RulesPage and CharacterGuidePage through the admin
settings API. While wiki-reader was absent, public server-info omitted all
three links. Installed only the bundled wiki-reader package, retaining every
previously installed package; the public projection then contained the three
correct canonical wiki URLs.

Signed in as the disposable ordinary player and followed each link from
`/welcome`. Welcome, Rules and Character Reference each rendered the expected
title and fixture body, with browser Back returning to the guide. Screenshot:
`work/onboarding/player-guide-character-reference.png`.

Restored the exact original onboarding settings and installed-package set using
their APIs, then verified all three guide links were absent again. The public
reference pages remain as harmless test fixtures. No production state changed.

### Live command audience validation (2026-10-20)

Three disposable accounts signed in through the real account API and obtained
single-use tokens for their existing characters. Separate gateway WebSockets
connected VeteranPilot, RoleplayPilot and RetryPilot. A unique `say` message
reached all three co-located characters. A unique `page RoleplayPilot=...`
message reached sender and recipient and was absent from RetryPilot's collected
output during the two-second observation window. Each test connection sent QUIT
and closed afterward. No credentials or handshake tokens were logged.

Evidence: `work/onboarding/audience-validation.json`; reproducible scratch
driver `work/onboarding/check-audiences.py`. This validates the room-speech and
private-page explanations; cross-room channel delivery remains a separate check.

Returning to Welcome after the guide-link test completed its asynchronous load
and removed all three restored/disabled links. The earlier observation was an
intermediate render, not a persistent stale-link failure.

### Cross-room channel audience validation (2026-10-20)

Created a dedicated randomly named channel in the isolated world and moved only
RoleplayPilot to another existing room. Three disposable players connected
through the real gateway. The exact lesson commands `@channel/list` and
`@channel/on NAME` discovered and joined the channel. `@chat NAME=...` delivered
a unique message to VeteranPilot and RoleplayPilot across rooms; RetryPilot,
who had not joined, did not receive it during the two-second observation window.

All three test connections quit and closed. RoleplayPilot's original location
was restored and read back for equality, and the dedicated channel was deleted.
Evidence: `work/onboarding/channel-audience-validation.json` and scratch driver
`work/onboarding/check-channel-audience.py`.

### Integrated CI outcome (2026-10-20)

For PR head `e524550fe`, .NET run `39396842975` passed build, formatting, core
tests, component tests, integration tests, Scene/Discord plugin tests and help
integrity. Parity run `39396842949`, table-layout run `39396842908`, deployment
impact and both CodeQL analysis runs also passed. The automated Claude review
job failed and CodeRabbit skipped the draft; neither is counted as substantive
review. Independent review of the latest keyboard changes and merge resolution
found no actionable defects. Subsequent documentation-only evidence edits do
not change the tested implementation.
